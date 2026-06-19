using System.Text.Json;

namespace LumiCodex.Upload;

internal static class ConfigurationStore
{
    private const string DefaultApiUrl = "https://api.lumicodex.com/";

    internal static string FilePath
    {
        get
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = AppContext.BaseDirectory;
            }

            return Path.Combine(basePath, "LumiCodex", "upload-config.json");
        }
    }

    internal static async Task<UploadConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return new UploadConfiguration(null, null);
        }

        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync(
            stream, UploadJsonContext.Default.UploadConfiguration, cancellationToken)
            ?? new UploadConfiguration(null, null);
    }

    internal static async Task SaveAsync(
        UploadConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        await using var stream = new FileStream(
            FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(
            stream, configuration, UploadJsonContext.Default.UploadConfiguration, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static Uri ResolveApiUrl(string? commandLine, UploadConfiguration configuration)
    {
        var value =
            FirstNonEmpty(
                commandLine,
                Environment.GetEnvironmentVariable("LUMICODEX_API_URL"),
                configuration.ApiUrl,
                DefaultApiUrl)!;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CliUsageException($"Invalid LumiCodex API URL '{value}'.");
        }
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new CliUsageException(
                "LumiCodex API URLs must use HTTPS unless they target the local machine.");
        }

        var normalized = uri.AbsoluteUri.EndsWith('/')
            ? uri.AbsoluteUri
            : $"{uri.AbsoluteUri}/";
        return new Uri(normalized, UriKind.Absolute);
    }

    internal static string? ResolveAccountId(
        string? commandLine,
        UploadConfiguration configuration)
        => FirstNonEmpty(
            commandLine,
            Environment.GetEnvironmentVariable("LUMICODEX_ACCOUNT_ID"),
            configuration.AccountId);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
