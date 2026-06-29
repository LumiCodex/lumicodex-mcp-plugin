using System.Text.Json.Serialization;

namespace LumiCodex.Upload;

internal sealed record UploadConfiguration(
    [property: JsonPropertyName("apiUrl")] string? ApiUrl,
    [property: JsonPropertyName("accountId")] string? AccountId);

internal sealed record CreateIngestsRequest(
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files);

internal sealed record ImageUploadPreparation(
    [property: JsonPropertyName("clientFileId")] int ClientFileId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("ingestId")] string? IngestId,
    [property: JsonPropertyName("uploadUrl")] string? UploadUrl,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("subType")] string? SubType,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("stackTrace")] string? StackTrace);

internal sealed record IngestStatus(
    [property: JsonPropertyName("ingestId")] string IngestId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("stackTrace")] string? StackTrace);

internal sealed record IngestStatusRequest(
    [property: JsonPropertyName("ingestIds")] IReadOnlyList<string> IngestIds);

internal sealed record IngestErrorRequest(
    [property: JsonPropertyName("errorMessage")] string ErrorMessage);

// The documents ephemeral/generate-upload-urls endpoint reuses the "urls" field to carry the
// file names to prepare upload slots for; it returns one presigned PUT URL and ephemeral id per
// entry, in request order.
internal sealed record DocumentUploadUrlsRequest(
    [property: JsonPropertyName("urls")] IReadOnlyList<string> Files);

internal sealed record DocumentUploadPreparation(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record DocumentUploadUrlsResponse(
    [property: JsonPropertyName("accountId")] string? AccountId,
    [property: JsonPropertyName("ids")] List<DocumentUploadPreparation>? Ids);

// Machine-readable results printed to stdout so an agent can feed ids into the documents/
// signatures MCP tools, and learn where downloaded results landed.
internal sealed record DocumentUploadResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record DocumentDownloadResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(UploadConfiguration))]
[JsonSerializable(typeof(CreateIngestsRequest))]
[JsonSerializable(typeof(List<ImageUploadPreparation>))]
[JsonSerializable(typeof(IngestStatusRequest))]
[JsonSerializable(typeof(List<IngestStatus>))]
[JsonSerializable(typeof(IngestErrorRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(DocumentUploadUrlsRequest))]
[JsonSerializable(typeof(DocumentUploadUrlsResponse))]
[JsonSerializable(typeof(List<DocumentUploadResult>))]
[JsonSerializable(typeof(List<DocumentDownloadResult>))]
internal sealed partial class UploadJsonContext : JsonSerializerContext;
