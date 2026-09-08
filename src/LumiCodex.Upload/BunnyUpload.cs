using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace LumiCodex.Upload;

internal sealed partial class LumiCodexApiClient
{
    internal async Task UploadBunnyAsync(string filePath, Uri endpoint, BunnyUploadData grant, CancellationToken cancellationToken)
    {
        if (endpoint.AbsoluteUri != "https://video.bunnycdn.com/tusupload" || grant.LibraryId <= 0 ||
            !Guid.TryParseExact(grant.VideoId, "D", out _) || grant.Signature.Length != 64 ||
            grant.Signature.Any(c => !char.IsAsciiHexDigit(c)) || grant.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new InvalidOperationException("The video upload authorization is invalid or expired.");
        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (file.Length is <= 0 or > 5_000_000_000) throw new InvalidOperationException("Videos must be between 1 byte and 5 GB.");
        using var create = TusRequest(HttpMethod.Post, endpoint, grant);
        create.Headers.Add("Upload-Length", file.Length.ToString(CultureInfo.InvariantCulture));
        var mime = Path.GetExtension(filePath).ToLowerInvariant() switch { ".mov" => "video/quicktime", ".webm" => "video/webm", _ => "video/mp4" };
        create.Headers.Add("Upload-Metadata", "filetype " + Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)) +
            ",title " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFileName(filePath))));
        create.Content = new ByteArrayContent([]);
        using var created = await SendTus(create, cancellationToken);
        var location = created.Headers.Location ?? throw new InvalidDataException("The video provider returned no upload location.");
        var uploadUrl = location.IsAbsoluteUri ? location : new Uri(endpoint, location);
        if (uploadUrl.Scheme != "https" || uploadUrl.Host != "video.bunnycdn.com" || !uploadUrl.IsDefaultPort ||
            uploadUrl.UserInfo.Length != 0 || uploadUrl.Fragment.Length != 0 || !uploadUrl.AbsolutePath.StartsWith("/tusupload/", StringComparison.Ordinal))
            throw new InvalidDataException("The video provider returned an invalid upload location.");
        var buffer = new byte[8 * 1024 * 1024];
        long offset = 0; var failures = 0;
        while (offset < file.Length)
        {
            try
            {
                if (failures > 0)
                {
                    using var head = TusRequest(HttpMethod.Head, uploadUrl, grant);
                    using var response = await SendTus(head, cancellationToken);
                    offset = TusOffset(response, file.Length);
                    if (offset == file.Length) break;
                }
                file.Position = offset;
                var count = (int)Math.Min(buffer.Length, file.Length - offset);
                await file.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken);
                using var patch = TusRequest(HttpMethod.Patch, uploadUrl, grant);
                patch.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
                patch.Content = new ByteArrayContent(buffer, 0, count);
                patch.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
                using var responsePatch = await SendTus(patch, cancellationToken);
                var next = TusOffset(responsePatch, file.Length);
                if (next != offset + count) throw new InvalidDataException("The video provider returned an invalid upload offset.");
                offset = next; failures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (IsRetryable(error) && ++failures < MaxUploadAttempts)
            { await Task.Delay(BackoffDelay(failures), cancellationToken); }
        }
    }

    private static long TusOffset(HttpResponseMessage response, long length)
    {
        if (!response.Headers.TryGetValues("Upload-Offset", out var values) ||
            !long.TryParse(values.SingleOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0 || offset > length)
            throw new InvalidDataException("The video provider returned an invalid upload offset.");
        return offset;
    }

    private static HttpRequestMessage TusRequest(HttpMethod method, Uri uri, BunnyUploadData grant)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("LibraryId", grant.LibraryId.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("VideoId", grant.VideoId);
        request.Headers.Add("AuthorizationSignature", grant.Signature);
        request.Headers.Add("AuthorizationExpire", grant.Expires.ToString(CultureInfo.InvariantCulture));
        return request;
    }

    private async Task<HttpResponseMessage> SendTus(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var response = await uploadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        // Provider bodies may contain capabilities. Only report the status.
        var message = $"Video upload failed ({status}).";
        throw IsTransientStatus(status) || status == 409 ? new TransientUploadException(message) : new PermanentUploadException(message);
    }
}
