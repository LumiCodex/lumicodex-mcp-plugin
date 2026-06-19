using System.Collections.Concurrent;
using System.Text.Json;

namespace LumiCodex.Upload;

internal static class UploaderApplication
{
    private const int MaxImageUploadsPerRequest = 500;
    private const int MaxImageUploadStatusesPerRequest = 1000;

    private static readonly HashSet<string> SupportedImageExtensions = new(
        [
            ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".jxl",
            ".tif", ".tiff", ".jp2", ".avif", ".bmp"
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
            throw new CliUsageException("At least one image file or folder is required.");
        }

        var (files, skippedFiles) = ResolveInputFiles(options.Inputs, options.Recursive);
        if (skippedFiles.Count > 0)
        {
            PrintSkippedFiles(skippedFiles);
        }
        if (files.Count == 0)
        {
            throw new CliUsageException("No supported image files were found.");
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

        using var api = new LumiCodexApiClient(apiUrl, apiKey.Trim());
        var work = new List<UploadWorkItem>(files.Count);
        foreach (var batch in files.Chunk(MaxImageUploadsPerRequest))
        {
            var batchFiles = batch.ToList();
            var descriptors = await api.CreateIngestsAsync(
                accountId,
                options.ContainerId,
                batchFiles.Select(file => Path.GetFileName(file)
                    ?? throw new CliUsageException($"File has no name: {file}")).ToList(),
                cancellationToken);
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
                    await api.UploadFileAsync(item.FilePath, item.UploadUrl, token);
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
                options.ProcessingTimeout,
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

        Console.WriteLine($"Completed {work.Count} image(s).");
        return 0;
    }

    private static (List<string> Files, List<string> SkippedFiles) ResolveInputFiles(
        IReadOnlyList<string> inputs,
        bool recursive)
    {
        var files = new List<string>();
        var skippedFiles = new List<string>();
        var seenFiles = new HashSet<string>(GetPathComparer());

        foreach (var input in inputs)
        {
            var path = Path.GetFullPath(input);
            if (File.Exists(path))
            {
                AddFile(path, files, skippedFiles, seenFiles);
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
                AddFile(Path.GetFullPath(file), files, skippedFiles, seenFiles);
            }
        }

        return (files, skippedFiles);
    }

    private static void AddFile(
        string path,
        List<string> files,
        List<string> skippedFiles,
        HashSet<string> seenFiles)
    {
        if (!SupportedImageExtensions.Contains(Path.GetExtension(path)))
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
        Console.WriteLine("Waiting for image processing.");
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
            $"Image processing did not complete within {timeout.TotalMinutes:0} minute(s).");
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

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            LumiCodex image uploader

            Upload:
              lumicodex-upload --container ID [options] INPUT [INPUT...]

            Configure:
              lumicodex-upload configure [--api-url URL] [--account ID]

            MCP headers:
              lumicodex-upload mcp-headers [--api-url URL]

            Inputs:
              Each input may be an image file or a folder. Folders are scanned only
              at their top level unless --recursive is specified.

            Options:
              --api-url URL          API URL. Defaults to LUMICODEX_API_URL,
                                     saved configuration, then https://api.lumicodex.com/
              --account ID           Account ID. Defaults to LUMICODEX_ACCOUNT_ID,
                                     then saved configuration.
              --container ID         Album container ID.
              --parallel N           Concurrent uploads, from 1 to 32. Default: 4.
              --timeout-minutes N    Processing timeout. Default: 30.
              --no-wait              Return after upload without waiting for processing.
              --publish              Publish after every image processes successfully.
              --recursive            Include images in nested input folders.
              -h, --help             Show this help.

            Authentication:
              LUMICODEX_API_KEY takes precedence over the operating-system credential
              store populated by 'lumicodex-upload configure'.
            """);
    }
}

internal sealed record UploadWorkItem(
    string FilePath,
    ImageUploadPreparation Descriptor,
    Uri UploadUrl);

internal sealed record UploadFailure(UploadWorkItem WorkItem, string Message);
