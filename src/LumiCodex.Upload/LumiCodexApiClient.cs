using System.Net.Http.Json;
using System.Text.Json;

namespace LumiCodex.Upload;

internal sealed class LumiCodexApiClient : IDisposable
{
    private const int MaxUploadAttempts = 4;
    private static readonly TimeSpan UploadRetryBaseDelay = TimeSpan.FromSeconds(2);

    // A per-attempt timeout that scales with file size so a stalled or dead connection is
    // abandoned instead of hanging forever, while a genuinely slow link still completes.
    // The floor throughput is deliberately conservative (~1 Mbps).
    private const long MinUploadBytesPerSecond = 128 * 1024;
    private static readonly TimeSpan MinUploadAttemptTimeout = TimeSpan.FromMinutes(2);

    private readonly HttpClient apiClient;
    private readonly HttpClient uploadClient;

    internal LumiCodexApiClient(Uri apiUrl, string apiKey)
    {
        apiClient = new HttpClient
        {
            BaseAddress = apiUrl,
            Timeout = TimeSpan.FromMinutes(2)
        };
        if (!string.IsNullOrEmpty(apiKey))
        {
            apiClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        uploadClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    internal async Task ValidateAuthenticationAsync(CancellationToken cancellationToken)
    {
        using var response = await apiClient.GetAsync(
            "health/hello2", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "API-key validation", cancellationToken);
    }

    internal async Task<List<ImageUploadPreparation>> CreateIngestsAsync(
        string accountId,
        string containerId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, ContainerPath(accountId, containerId, "ingests"))
        {
            Content = JsonContent.Create(
                new CreateIngestsRequest(fileNames),
                UploadJsonContext.Default.CreateIngestsRequest)
        };
        using var response = await apiClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "creating image ingests", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
            stream,
            UploadJsonContext.Default.ListImageUploadPreparation,
            cancellationToken) ?? [];
    }

    internal async Task UploadFileAsync(
        string filePath,
        Uri uploadUrl,
        CancellationToken cancellationToken)
    {
        var attemptTimeout = ComputeAttemptTimeout(new FileInfo(filePath).Length);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await UploadOnceAsync(filePath, uploadUrl, attemptTimeout, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < MaxUploadAttempts && IsRetryable(exception))
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                // The user did not cancel, so this is a per-attempt timeout on the final attempt.
                // Surface it as a failure rather than letting the caller treat it as cancellation.
                throw new TimeoutException(
                    $"Upload of '{Path.GetFileName(filePath)}' timed out after {MaxUploadAttempts} attempt(s).",
                    exception);
            }
        }
    }

    private async Task UploadOnceAsync(
        string filePath,
        Uri uploadUrl,
        TimeSpan attemptTimeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(attemptTimeout);
        var token = timeoutCts.Token;

        await using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new StreamContent(file, 128 * 1024);
        content.Headers.ContentLength = file.Length;
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = content
        };
        using var response = await uploadClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, token);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await DescribeFailureAsync(
            response, $"uploading '{Path.GetFileName(filePath)}'", token);
        throw IsTransientStatus((int)response.StatusCode)
            ? new TransientUploadException(message)
            : new PermanentUploadException(message);
    }

    private static bool IsRetryable(Exception exception)
        => exception is TransientUploadException
            or HttpRequestException
            or IOException
            or OperationCanceledException;

    private static bool IsTransientStatus(int statusCode)
        => statusCode == 408 || statusCode == 429 || statusCode >= 500;

    private static TimeSpan ComputeAttemptTimeout(long fileLength)
    {
        var scaled = TimeSpan.FromSeconds((double)fileLength / MinUploadBytesPerSecond);
        return scaled > MinUploadAttemptTimeout ? scaled : MinUploadAttemptTimeout;
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var seconds = UploadRetryBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.5 * seconds;
        return TimeSpan.FromSeconds(seconds + jitter);
    }

    internal async Task MarkIngestErrorAsync(
        string accountId,
        string containerId,
        string ingestId,
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            ContainerPath(
                accountId,
                containerId,
                $"ingests/{Uri.EscapeDataString(ingestId)}/error"))
        {
            Content = JsonContent.Create(
                new IngestErrorRequest(message),
                UploadJsonContext.Default.IngestErrorRequest)
        };
        using var response = await apiClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, $"marking ingest '{ingestId}' as failed", cancellationToken);
    }

    internal async Task<List<IngestStatus>> GetIngestStatusesAsync(
        string accountId,
        string containerId,
        IReadOnlyList<string> ingestIds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ContainerPath(accountId, containerId, "ingests/statuses"))
        {
            Content = JsonContent.Create(
                new IngestStatusRequest(ingestIds),
                UploadJsonContext.Default.IngestStatusRequest)
        };
        using var response = await apiClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "reading image-processing tasks", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
            stream,
            UploadJsonContext.Default.ListIngestStatus,
            cancellationToken) ?? [];
    }

    internal async Task PublishAsync(
        string accountId,
        string containerId,
        CancellationToken cancellationToken)
    {
        using var response = await apiClient.PostAsync(
            ContainerPath(accountId, containerId, "publish"),
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, "publishing the container", cancellationToken);
    }

    internal async Task<DocumentUploadUrlsResponse> GenerateDocumentUploadUrlsAsync(
        string accountId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"accounts/{Uri.EscapeDataString(accountId)}/documents/ephemeral/generate-upload-urls")
        {
            Content = JsonContent.Create(
                new DocumentUploadUrlsRequest(fileNames),
                UploadJsonContext.Default.DocumentUploadUrlsRequest)
        };
        using var response = await apiClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "creating document upload URLs", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
            stream,
            UploadJsonContext.Default.DocumentUploadUrlsResponse,
            cancellationToken)
            ?? throw new InvalidOperationException("The API returned no document upload URLs.");
    }

    // Downloads a presigned URL to a file in targetDirectory, returning the saved path. The name
    // is taken from preferredName, then the response Content-Disposition, then the URL, then a
    // content-type-based fallback. Retries transient failures like the upload path does.
    internal async Task<string> DownloadDocumentAsync(
        Uri url,
        string targetDirectory,
        string? preferredName,
        CancellationToken cancellationToken)
    {
        string? reservedPath = preferredName is null
            ? null
            : ReserveUniquePath(targetDirectory, preferredName);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    reservedPath = await DownloadOnceAsync(url, targetDirectory, reservedPath, cancellationToken);
                    return reservedPath;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (attempt < MaxUploadAttempts && IsRetryable(exception))
                {
                    await Task.Delay(BackoffDelay(attempt), cancellationToken);
                }
                catch (OperationCanceledException exception)
                {
                    throw new TimeoutException(
                        $"Download from '{url}' timed out after {MaxUploadAttempts} attempt(s).",
                        exception);
                }
            }
        }
        catch
        {
            DeleteQuietly(reservedPath);
            throw;
        }
    }

    private async Task<string> DownloadOnceAsync(
        Uri url,
        string targetDirectory,
        string? reservedPath,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(MinUploadAttemptTimeout);
        var token = timeoutCts.Token;

        using var response = await uploadClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            var message = await DescribeFailureAsync(response, $"downloading from '{url}'", token);
            throw IsTransientStatus((int)response.StatusCode)
                ? new TransientUploadException(message)
                : new PermanentUploadException(message);
        }

        var destinationPath = reservedPath
            ?? ReserveUniquePath(targetDirectory, ResolveDownloadName(response, url));

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using (var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 128 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(destination, token);
        }

        return destinationPath;
    }

    private static string ResolveDownloadName(HttpResponseMessage response, Uri url)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var fromDisposition = disposition?.FileNameStar ?? disposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fromDisposition))
        {
            return SanitizeFileName(fromDisposition.Trim('"'));
        }

        var fromUrl = SanitizeFileName(Path.GetFileName(url.AbsolutePath));
        if (Path.HasExtension(fromUrl))
        {
            return fromUrl;
        }

        var extension = ExtensionForContentType(response.Content.Headers.ContentType?.MediaType);
        return $"{(string.IsNullOrWhiteSpace(fromUrl) ? "document" : fromUrl)}{extension}";
    }

    private static string ExtensionForContentType(string? mediaType) => mediaType switch
    {
        "application/pdf" => ".pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "text/html" => ".html",
        "text/plain" => ".txt",
        "application/json" => ".json",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        _ => string.Empty
    };

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/').Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "document" : name;
    }

    private static readonly object PathReservationLock = new();

    private static string ReserveUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        lock (PathReservationLock)
        {
            for (var index = 0; ; index++)
            {
                var candidate = Path.Combine(
                    directory, index == 0 ? fileName : $"{baseName}-{index}{extension}");
                try
                {
                    using var _ = new FileStream(candidate, FileMode.CreateNew);
                    return candidate;
                }
                catch (IOException)
                {
                    // Name taken; try the next suffix.
                }
            }
        }
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of a partial download.
        }
    }

    private static string ContainerPath(
        string accountId,
        string containerId,
        string operation)
        => $"accounts/{Uri.EscapeDataString(accountId)}/containers/" +
           $"{Uri.EscapeDataString(containerId)}/{operation}";

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            await DescribeFailureAsync(response, operation, cancellationToken));
    }

    private static async Task<string> DescribeFailureAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 500)
        {
            body = body[..500];
        }

        return $"LumiCodex returned {(int)response.StatusCode} ({response.ReasonPhrase}) while {operation}" +
            (string.IsNullOrWhiteSpace(body) ? "." : $": {body}");
    }

    public void Dispose()
    {
        apiClient.Dispose();
        uploadClient.Dispose();
    }
}

internal sealed class TransientUploadException(string message) : Exception(message);

internal sealed class PermanentUploadException(string message) : Exception(message);
