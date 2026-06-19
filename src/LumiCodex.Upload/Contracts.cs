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
internal sealed partial class UploadJsonContext : JsonSerializerContext;
