using System.Collections.Concurrent;
using System.Text.Json;

namespace LumiCodex.Upload;

internal static class UploaderApplication
{
    private const int MaxImageUploadsPerRequest = 500;
    private const int MaxImageUploadStatusesPerRequest = 1000;
    private const int MaxDocumentUploadsPerRequest = 100;

    private static readonly HashSet<string> SupportedMediaExtensions = new(
        [
            ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".jxl",
            ".tif", ".tiff", ".jp2", ".avif", ".bmp",
            ".mp4", ".mov", ".m4v", ".webm"
        ],
        StringComparer.OrdinalIgnoreCase);

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }
            if (options.Command == UploaderCommand.EncodeUltra)
            {
                var results = await UltraEncoder.EncodeAsync(options, cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(
                    results,
                    UploadJsonContext.Default.ListUltraEncodeResult));
                return 0;
            }

            var savedConfiguration = await ConfigurationStore.LoadAsync(cancellationToken);
            var apiUrl = ConfigurationStore.ResolveApiUrl(options.ApiUrl, savedConfiguration);
            var credentialStore = CredentialStore.Create();

            if (options.Configure)
            {
                return await ConfigureAsync(
                    options,
                    savedConfiguration,
                    apiUrl,
                    credentialStore,
                    cancellationToken);
            }
            if (options.EmitMcpHeaders)
            {
                return await EmitMcpHeadersAsync(
                    options,
                    apiUrl,
                    credentialStore,
                    cancellationToken);
            }
            if (options.DocumentsUpload)
            {
                return await UploadDocumentsAsync(
                    options,
                    savedConfiguration,
                    apiUrl,
                    credentialStore,
                    cancellationToken);
            }
            if (options.DocumentsDownload)
            {
                return await DownloadDocumentsAsync(options, cancellationToken);
            }
            if (options.Command is UploaderCommand.WhoAmI or
                UploaderCommand.AlbumsList or
                UploaderCommand.AlbumsCreate)
            {
                return await RunAccountCommandAsync(
                    options,
                    savedConfiguration,
                    apiUrl,
                    credentialStore,
                    cancellationToken);
            }

            return await UploadAsync(
                options,
                savedConfiguration,
                apiUrl,
                credentialStore,
                cancellationToken);
        }
        catch (CliUsageException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            Console.Error.WriteLine("Run 'lumicodex-upload --help' for usage.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ConfigureAsync(
        CliOptions options,
        UploadConfiguration savedConfiguration,
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        if (options.Inputs.Count > 0 ||
            options.ContainerId is not null ||
            options.Publish ||
            options.Recursive ||
            !options.WaitForProcessing)
        {
            throw new CliUsageException(
                "The configure command accepts only --api-url and --account.");
        }

        var apiKey = Environment.GetEnvironmentVariable("LUMICODEX_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = ReadSecret("LumiCodex API key: ");
        }
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0)
        {
            throw new CliUsageException("An API key is required.");
        }

        using (var api = new LumiCodexApiClient(apiUrl, apiKey))
        {
            await api.ValidateAuthenticationAsync(cancellationToken);
        }

        await credentialStore.WriteAsync(apiUrl, apiKey, cancellationToken);

        var accountId = ConfigurationStore.ResolveAccountId(options.AccountId, savedConfiguration);
        await ConfigurationStore.SaveAsync(
            new UploadConfiguration(apiUrl.AbsoluteUri, accountId),
            cancellationToken);

        Console.WriteLine($"Configured API: {apiUrl}");
        Console.WriteLine(accountId is null
            ? "No default account configured."
            : $"Configured account: {accountId}");
        Console.WriteLine("API key validated and saved in the operating-system credential store.");
        return 0;
    }

    private static async Task<int> EmitMcpHeadersAsync(
        CliOptions options,
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        if (options.Inputs.Count > 0 ||
            options.AccountId is not null ||
            options.ContainerId is not null ||
            options.Publish ||
            options.Recursive ||
            !options.WaitForProcessing)
        {
            throw new CliUsageException(
                "The mcp-headers command accepts only --api-url.");
        }

        var apiKey = Environment.GetEnvironmentVariable("LUMICODEX_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await credentialStore.ReadAsync(apiUrl, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new CliUsageException(
                "No API key is available. Set LUMICODEX_API_KEY or run 'lumicodex-upload configure'.");
        }

        var headers = new Dictionary<string, string>
        {
            ["x-api-key"] = apiKey.Trim()
        };
        Console.WriteLine(JsonSerializer.Serialize(
            headers,
            UploadJsonContext.Default.DictionaryStringString));
        return 0;
    }

    private static async Task<int> UploadDocumentsAsync(
        CliOptions options,
        UploadConfiguration savedConfiguration,
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var accountId = ConfigurationStore.ResolveAccountId(options.AccountId, savedConfiguration);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new CliUsageException(
                "An account is required. Use --account, LUMICODEX_ACCOUNT_ID, or configure a default.");
        }
        if (options.Inputs.Count == 0)
        {
            throw new CliUsageException("At least one document file or folder is required.");
        }

        // Documents may be any type, so unlike images there is no extension allow-list.
        var (files, _) = ResolveInputFiles(options.Inputs, options.Recursive, allowedExtensions: null);
        if (files.Count == 0)
        {
            throw new CliUsageException("No files were found at the supplied paths.");
        }

        var apiKey = await ResolveApiKeyAsync(apiUrl, credentialStore, cancellationToken);
        using var api = new LumiCodexApiClient(apiUrl, apiKey);

        var work = new List<DocumentUploadWorkItem>(files.Count);
        foreach (var batch in files.Chunk(MaxDocumentUploadsPerRequest))
        {
            var batchFiles = batch.ToList();
            var response = await api.GenerateDocumentUploadUrlsAsync(
                accountId,
                batchFiles.Select(file => Path.GetFileName(file)
                    ?? throw new CliUsageException($"File has no name: {file}")).ToList(),
                cancellationToken);
            work.AddRange(CorrelateDocuments(batchFiles, response.Ids));
        }

        Console.Error.WriteLine(
            $"Created {work.Count} ephemeral document(s). Uploading with concurrency {options.Parallelism}.");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            work,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (item, token) =>
            {
                try
                {
                    Console.Error.WriteLine($"Uploading {Path.GetFileName(item.FilePath)}");
                    await api.UploadFileAsync(item.FilePath, item.UploadUrl, token);
                    Console.Error.WriteLine($"Uploaded  {Path.GetFileName(item.FilePath)} -> {item.DocumentId}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add($"{Path.GetFileName(item.FilePath)}: {exception.Message}");
                    Console.Error.WriteLine(
                        $"Upload failed {Path.GetFileName(item.FilePath)}: {exception.Message}");
                }
            });

        if (!failures.IsEmpty)
        {
            Console.Error.WriteLine($"{failures.Count} document upload(s) failed.");
            return 3;
        }

        var results = work
            .Select(item => new DocumentUploadResult(item.DocumentId, Path.GetFileName(item.FilePath)))
            .ToList();
        Console.WriteLine(JsonSerializer.Serialize(results, UploadJsonContext.Default.ListDocumentUploadResult));
        Console.Error.WriteLine($"Completed {results.Count} document(s).");
        return 0;
    }

    private static async Task<int> DownloadDocumentsAsync(
        CliOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Inputs.Count == 0)
        {
            throw new CliUsageException("At least one download URL is required.");
        }

        var urls = new List<Uri>(options.Inputs.Count);
        foreach (var input in options.Inputs)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new CliUsageException($"Not an absolute http(s) download URL: {input}");
            }
            urls.Add(uri);
        }

        var (directory, forcedName) = ResolveDownloadTarget(options.OutputPath, urls.Count);

        // The API key is not used for downloads: the URLs are already presigned. The base URL is
        // irrelevant too, so any absolute Uri works to construct the client.
        using var api = new LumiCodexApiClient(new Uri("https://api.lumicodex.com/"), apiKey: string.Empty);

        var results = new System.Collections.Concurrent.ConcurrentBag<DocumentDownloadResult>();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            urls,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (url, token) =>
            {
                try
                {
                    var savedPath = await api.DownloadDocumentAsync(url, directory, forcedName, token);
                    results.Add(new DocumentDownloadResult(Path.GetFileName(savedPath), savedPath));
                    Console.Error.WriteLine($"Downloaded {Path.GetFileName(savedPath)} -> {savedPath}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add($"{url}: {exception.Message}");
                    Console.Error.WriteLine($"Download failed {url}: {exception.Message}");
                }
            });

        if (!failures.IsEmpty)
        {
            Console.Error.WriteLine($"{failures.Count} download(s) failed.");
            return 3;
        }

        Console.WriteLine(JsonSerializer.Serialize(
            results.ToList(), UploadJsonContext.Default.ListDocumentDownloadResult));
        Console.Error.WriteLine($"Completed {results.Count} download(s).");
        return 0;
    }

    private static (string Directory, string? ForcedName) ResolveDownloadTarget(
        string? outputPath,
        int urlCount)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return (Directory.GetCurrentDirectory(), null);
        }

        var fullPath = Path.GetFullPath(outputPath);
        if (Directory.Exists(fullPath))
        {
            return (fullPath, null);
        }

        // A single URL plus a path that looks like a file name is treated as an explicit
        // destination file; anything else is treated as a directory to create.
        if (urlCount == 1 && Path.HasExtension(fullPath))
        {
            var directory = Path.GetDirectoryName(fullPath);
            return (string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory,
                Path.GetFileName(fullPath));
        }

        return (fullPath, null);
    }

    private static async Task<string> ResolveApiKeyAsync(
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("LUMICODEX_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await credentialStore.ReadAsync(apiUrl, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new CliUsageException(
                "No API key is available. Set LUMICODEX_API_KEY or run 'lumicodex-upload configure'.");
        }

        return apiKey.Trim();
    }

    private static async Task<int> RunAccountCommandAsync(
        CliOptions options,
        UploadConfiguration savedConfiguration,
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var accountId = ConfigurationStore.ResolveAccountId(options.AccountId, savedConfiguration);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new CliUsageException(
                "An account is required. Use --account, LUMICODEX_ACCOUNT_ID, or configure a default.");
        }

        var apiKey = await ResolveApiKeyAsync(apiUrl, credentialStore, cancellationToken);
        using var api = new LumiCodexApiClient(apiUrl, apiKey);
        switch (options.Command)
        {
            case UploaderCommand.WhoAmI:
            {
                var account = await api.GetAccountAsync(accountId, cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(account, UploadJsonContext.Default.AccountSummary));
                return 0;
            }
            case UploaderCommand.AlbumsList:
            {
                var albums = await api.ListAlbumsAsync(accountId, cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(albums, UploadJsonContext.Default.ListAlbumSummary));
                return 0;
            }
            case UploaderCommand.AlbumsCreate:
            {
                var album = await api.CreateAlbumAsync(
                    accountId,
                    options.Name!,
                    options.Access == "public",
                    cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(album, UploadJsonContext.Default.AlbumSummary));
                return 0;
            }
            default:
                throw new InvalidOperationException($"Unsupported account command: {options.Command}");
        }
    }

    private static List<DocumentUploadWorkItem> CorrelateDocuments(
        IReadOnlyList<string> files,
        IReadOnlyList<DocumentUploadPreparation>? descriptors)
    {
        if (descriptors is null || descriptors.Count != files.Count)
        {
            throw new InvalidOperationException(
                $"The API returned {descriptors?.Count ?? 0} upload URL(s) for {files.Count} file(s).");
        }

        var work = new List<DocumentUploadWorkItem>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var descriptor = descriptors[i];
            if (string.IsNullOrWhiteSpace(descriptor.Id))
            {
                throw new InvalidOperationException($"The API did not return a document id for '{files[i]}'.");
            }
            if (!Uri.TryCreate(descriptor.Url, UriKind.Absolute, out var uploadUrl))
            {
                throw new InvalidOperationException($"The API did not return a valid upload URL for '{files[i]}'.");
            }

            work.Add(new DocumentUploadWorkItem(files[i], descriptor.Id, uploadUrl));
        }

        return work;
    }

    private static async Task<int> UploadAsync(
        CliOptions options,
        UploadConfiguration savedConfiguration,
        Uri apiUrl,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        var accountId = ConfigurationStore.ResolveAccountId(options.AccountId, savedConfiguration);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new CliUsageException(
                "An account is required. Use --account, LUMICODEX_ACCOUNT_ID, or configure a default.");
        }
        if (string.IsNullOrWhiteSpace(options.ContainerId))
        {
            throw new CliUsageException("--container is required.");
        }
        if (options.Inputs.Count == 0)
        {
            throw new CliUsageException("At least one photo, video or folder is required.");
        }

        var (files, skippedFiles) = ResolveInputFiles(options.Inputs, options.Recursive, SupportedMediaExtensions);
        if (skippedFiles.Count > 0)
        {
            PrintSkippedFiles(skippedFiles);
        }
        if (files.Count == 0)
        {
            throw new CliUsageException("No supported media files were found.");
        }

        foreach (var file in files.Where(IsVideo))
            if (new FileInfo(file).Length is <= 0 or > 5_000_000_000L)
                throw new CliUsageException($"Video files must be between 1 byte and 5 GB: {file}");

        var apiKey = await ResolveApiKeyAsync(apiUrl, credentialStore, cancellationToken);
        using var api = new LumiCodexApiClient(apiUrl, apiKey);
        var work = new List<UploadWorkItem>(files.Count);
        foreach (var batch in files.Chunk(MaxImageUploadsPerRequest))
        {
            var batchFiles = batch.ToList();
            var descriptors = await api.CreateIngestsAsync(
                accountId,
                options.ContainerId,
                batchFiles.Select(file => Path.GetFileName(file)
                    ?? throw new CliUsageException($"File has no name: {file}")).ToList(),
                cancellationToken, batchFiles.Select(file => new FileInfo(file).Length).ToList());
            work.AddRange(Correlate(batchFiles, descriptors));
        }

        Console.WriteLine($"Created {work.Count} ingest record(s). Uploading with concurrency {options.Parallelism}.");
        var failures = new ConcurrentBag<UploadFailure>();
        await Parallel.ForEachAsync(
            work,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (item, token) =>
            {
                try
                {
                    Console.WriteLine($"Uploading {Path.GetFileName(item.FilePath)}");
                    if (item.Descriptor.BunnyUpload is { } bunny)
                        await api.UploadBunnyAsync(item.FilePath, item.UploadUrl, bunny, token);
                    else await api.UploadFileAsync(item.FilePath, item.UploadUrl, token);
                    Console.WriteLine($"Uploaded  {Path.GetFileName(item.FilePath)}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new UploadFailure(item, exception.Message));
                    Console.Error.WriteLine(
                        $"Upload failed {Path.GetFileName(item.FilePath)}: {exception.Message}");
                    try
                    {
                        await api.MarkIngestErrorAsync(
                            accountId,
                            options.ContainerId,
                            item.Descriptor.IngestId!,
                            exception.Message,
                            token);
                    }
                    catch (Exception markException) when (markException is not OperationCanceledException)
                    {
                        Console.Error.WriteLine(
                            $"Could not mark ingest {item.Descriptor.IngestId} as failed: {markException.Message}");
                    }
                }
            });

        if (!failures.IsEmpty)
        {
            Console.Error.WriteLine($"{failures.Count} upload(s) failed.");
            return 3;
        }

        if (options.WaitForProcessing)
        {
            var processingErrors = await WaitForProcessingAsync(
                api,
                accountId,
                options.ContainerId,
                work.Select(item => item.Descriptor.IngestId!).ToHashSet(StringComparer.Ordinal),
                !options.HasExplicitProcessingTimeout && files.Any(IsVideo)
                    ? TimeSpan.FromHours(50) : options.ProcessingTimeout,
                cancellationToken);
            if (processingErrors.Count > 0)
            {
                foreach (var error in processingErrors)
                {
                    Console.Error.WriteLine(
                        $"Processing failed {error.IngestId} ({error.FileName}): {error.Message ?? "Unknown error"}");
                }
                return 4;
            }
        }

        if (options.Publish)
        {
            await api.PublishAsync(accountId, options.ContainerId, cancellationToken);
            Console.WriteLine($"Published container {options.ContainerId}.");
        }

        Console.WriteLine($"Completed {work.Count} media item(s).");
        return 0;
    }

    private static (List<string> Files, List<string> SkippedFiles) ResolveInputFiles(
        IReadOnlyList<string> inputs,
        bool recursive,
        IReadOnlySet<string>? allowedExtensions)
    {
        var files = new List<string>();
        var skippedFiles = new List<string>();
        var seenFiles = new HashSet<string>(GetPathComparer());

        foreach (var input in inputs)
        {
            var path = Path.GetFullPath(input);
            if (File.Exists(path))
            {
                AddFile(path, files, skippedFiles, seenFiles, allowedExtensions);
                continue;
            }

            if (!Directory.Exists(path))
            {
                throw new CliUsageException($"Input path not found: {path}");
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchType = MatchType.Simple
            };
            foreach (var file in Directory
                .EnumerateFiles(path, "*", enumerationOptions)
                .OrderBy(file => file, GetPathComparer()))
            {
                AddFile(Path.GetFullPath(file), files, skippedFiles, seenFiles, allowedExtensions);
            }
        }

        return (files, skippedFiles);
    }

    private static void AddFile(
        string path,
        List<string> files,
        List<string> skippedFiles,
        HashSet<string> seenFiles,
        IReadOnlySet<string>? allowedExtensions)
    {
        if (allowedExtensions is not null && !allowedExtensions.Contains(Path.GetExtension(path)))
        {
            skippedFiles.Add(path);
            return;
        }

        if (seenFiles.Add(path))
        {
            files.Add(path);
        }
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static void PrintSkippedFiles(IReadOnlyList<string> skippedFiles)
    {
        const int displayLimit = 10;
        Console.WriteLine($"Skipped {skippedFiles.Count} unsupported file(s):");
        foreach (var file in skippedFiles.Take(displayLimit))
        {
            Console.WriteLine($"  {file}");
        }
        if (skippedFiles.Count > displayLimit)
        {
            Console.WriteLine($"  ... and {skippedFiles.Count - displayLimit} more");
        }
    }

    private static List<UploadWorkItem> Correlate(
        IReadOnlyList<string> files,
        IReadOnlyList<ImageUploadPreparation> descriptors)
    {
        if (descriptors.Count != files.Count)
        {
            throw new InvalidOperationException(
                $"The API returned {descriptors.Count} upload descriptor(s) for {files.Count} file(s).");
        }

        var result = new UploadWorkItem?[files.Count];
        var usedIndexes = new HashSet<int>();
        foreach (var descriptor in descriptors)
        {
            var index = descriptor.ClientFileId;
            if (index < 0 ||
                index >= files.Count ||
                !usedIndexes.Add(index))
            {
                throw new InvalidOperationException(
                    $"The API returned invalid clientFileId '{descriptor.ClientFileId}'.");
            }
            if (string.IsNullOrWhiteSpace(descriptor.IngestId))
            {
                throw new InvalidOperationException(
                    $"The API did not return an ingestId for clientFileId '{descriptor.ClientFileId}'.");
            }
            if (!Uri.TryCreate(descriptor.UploadUrl, UriKind.Absolute, out var uploadUrl))
            {
                throw new InvalidOperationException(
                    $"The API did not return a valid uploadUrl for clientFileId '{descriptor.ClientFileId}'.");
            }
            if (descriptor.ExpiresAt is not null && descriptor.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException(
                    $"The upload URL for clientFileId '{descriptor.ClientFileId}' has expired.");
            }

            result[index] = new UploadWorkItem(files[index], descriptor, uploadUrl);
        }

        return result
            .Select(item => item ?? throw new InvalidOperationException(
                "The API response did not contain every clientFileId."))
            .ToList();
    }

    private static async Task<List<IngestStatus>> WaitForProcessingAsync(
        LumiCodexApiClient api,
        string accountId,
        string containerId,
        HashSet<string> ingestIds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Waiting for media processing.");
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var statuses = new List<IngestStatus>(ingestIds.Count);
            foreach (var batch in ingestIds.Chunk(MaxImageUploadStatusesPerRequest))
            {
                statuses.AddRange(await api.GetIngestStatusesAsync(
                    accountId,
                    containerId,
                    batch.ToList(),
                    cancellationToken));
            }

            var errors = statuses
                .Where(status => string.Equals(status.State, "error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var completed = statuses.Count(status =>
                string.Equals(status.State, "complete", StringComparison.OrdinalIgnoreCase));
            var processing = statuses.Count - completed - errors.Count;

            if (processing == 0)
            {
                return errors;
            }

            Console.WriteLine(
                $"Processing: {processing}; completed: {completed}; failed: {errors.Count}");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException(
            $"Media processing did not complete within {timeout.TotalMinutes:0} minute(s).");
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new CliUsageException(
                "Cannot prompt for an API key with redirected input. Set LUMICODEX_API_KEY.");
        }

        Console.Write(prompt);
        var value = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string([.. value]);
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Count > 0)
                {
                    value.RemoveAt(value.Count - 1);
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                value.Add(key.KeyChar);
            }
        }
    }

    internal static bool IsVideo(string file) => Path.GetExtension(file).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".webm";

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LumiCodex uploader

            Upload photos and videos to an album:
              lumicodex-upload --container ID [options] INPUT [INPUT...]

            Upload documents to ephemeral storage (for the signatures/documents MCP tools):
              lumicodex-upload documents upload [options] FILE [FILE...]
                Prints a JSON array of { id, name } to stdout. Feed each id into a
                documents/signatures tool (e.g. Source.documentId, or fetch a URL with
                documents_get_download_url for envelopes_add_document).

            Download processed results back to disk:
              lumicodex-upload documents download [--out PATH] [options] URL [URL...]
                URLs are the presigned download URLs returned by the documents/signatures
                tools (e.g. GenerateUrlOutput.url, envelopes_get_downloads).

            Configure:
              lumicodex-upload configure [--api-url URL] [--account ID]

            MCP headers:
              lumicodex-upload mcp-headers [--api-url URL]

            Verify an account and manage albums (prints JSON to stdout):
              lumicodex-upload whoami [--account ID]
              lumicodex-upload albums list [--account ID]
              lumicodex-upload albums create --name NAME --access public|private [--account ID]

            Encode Lightroom's 16-bit, color-managed render for an Ultra upload:
              lumicodex-upload encode ultra --codec avif|jxl --out DIR [--vips PATH] [--cjxl PATH] INPUT...

            Inputs:
              For image upload and 'documents upload', each input may be a file or a folder.
              Folders are scanned only at their top level unless --recursive is specified
              (media upload keeps supported photo types and MP4/MOV/M4V/WebM videos; document upload takes any file).

            Options:
              --api-url URL          API URL. Defaults to LUMICODEX_API_URL,
                                     saved configuration, then https://api.lumicodex.com/
              --account ID           Account ID. Defaults to LUMICODEX_ACCOUNT_ID,
                                     then saved configuration.
              --container ID         Album container ID (image upload only).
              --out PATH             'documents download' destination. A directory, or a
                                     file path when downloading a single URL. Default: cwd.
              --parallel N           Concurrent transfers, from 1 to 32. Default: 4.
              --timeout-minutes N    Processing timeout. Default: 30 minutes; 3000 for video.
              --no-wait              Return after upload without waiting for processing.
              --publish              Publish after every image processes successfully.
              --recursive            Include files in nested input folders.
              --name NAME            New album name ('albums create' only).
              --access VALUE         New album access: public or private.
              --codec VALUE          Ultra codec: avif or jxl.
              --vips PATH            libvips executable. Defaults to LUMICODEX_VIPS_PATH,
                                     then vips on PATH.
              --cjxl PATH            JPEG XL encoder. Defaults to LUMICODEX_CJXL_PATH,
                                     then cjxl on PATH (JXL only).
              -h, --help             Show this help.

            Authentication:
              LUMICODEX_API_KEY takes precedence over the operating-system credential
              store populated by 'lumicodex-upload configure'. Document downloads use the
              presigned URL only and need no API key.
            """);
    }
}

internal sealed record UploadWorkItem(
    string FilePath,
    ImageUploadPreparation Descriptor,
    Uri UploadUrl);

internal sealed record UploadFailure(UploadWorkItem WorkItem, string Message);

internal sealed record DocumentUploadWorkItem(
    string FilePath,
    string DocumentId,
    Uri UploadUrl);
