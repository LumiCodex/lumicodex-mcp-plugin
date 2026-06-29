namespace LumiCodex.Upload;

internal sealed record CliOptions(
    bool Configure,
    bool EmitMcpHeaders,
    bool ShowHelp,
    string? ApiUrl,
    string? AccountId,
    string? ContainerId,
    int Parallelism,
    TimeSpan ProcessingTimeout,
    bool WaitForProcessing,
    bool Publish,
    bool Recursive,
    bool DocumentsUpload,
    bool DocumentsDownload,
    string? OutputPath,
    IReadOnlyList<string> Inputs)
{
    internal static CliOptions Parse(string[] args)
    {
        var first = args.FirstOrDefault();
        var configure = string.Equals(first, "configure", StringComparison.OrdinalIgnoreCase);
        var emitMcpHeaders = string.Equals(first, "mcp-headers", StringComparison.OrdinalIgnoreCase);
        var documents = string.Equals(first, "documents", StringComparison.OrdinalIgnoreCase);
        var documentsUpload = false;
        var documentsDownload = false;

        var index = configure || emitMcpHeaders ? 1 : 0;
        if (documents)
        {
            var subcommand = args.Length > 1 ? args[1] : null;
            if (string.Equals(subcommand, "upload", StringComparison.OrdinalIgnoreCase))
            {
                documentsUpload = true;
            }
            else if (string.Equals(subcommand, "download", StringComparison.OrdinalIgnoreCase))
            {
                documentsDownload = true;
            }
            else
            {
                throw new CliUsageException("documents requires a subcommand: 'upload' or 'download'.");
            }
            index = 2;
        }

        var showHelp = false;
        string? apiUrl = null;
        string? accountId = null;
        string? containerId = null;
        string? outputPath = null;
        var parallelism = 4;
        var timeout = TimeSpan.FromMinutes(30);
        var wait = true;
        var publish = false;
        var recursive = false;
        var inputs = new List<string>();

        while (index < args.Length)
        {
            var argument = args[index++];
            switch (argument)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--api-url":
                    apiUrl = ReadValue(args, ref index, argument);
                    break;
                case "--account":
                    accountId = ReadValue(args, ref index, argument);
                    break;
                case "--container":
                    containerId = ReadValue(args, ref index, argument);
                    break;
                case "--out":
                    outputPath = ReadValue(args, ref index, argument);
                    break;
                case "--parallel":
                    if (!int.TryParse(ReadValue(args, ref index, argument), out parallelism) ||
                        parallelism is < 1 or > 32)
                    {
                        throw new CliUsageException("--parallel must be between 1 and 32.");
                    }
                    break;
                case "--timeout-minutes":
                    if (!int.TryParse(ReadValue(args, ref index, argument), out var minutes) ||
                        minutes is < 1 or > 1440)
                    {
                        throw new CliUsageException("--timeout-minutes must be between 1 and 1440.");
                    }
                    timeout = TimeSpan.FromMinutes(minutes);
                    break;
                case "--no-wait":
                    wait = false;
                    break;
                case "--publish":
                    publish = true;
                    break;
                case "--recursive":
                    recursive = true;
                    break;
                case "--":
                    while (index < args.Length)
                    {
                        inputs.Add(args[index++]);
                    }
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new CliUsageException($"Unknown option '{argument}'.");
                    }
                    inputs.Add(argument);
                    break;
            }
        }

        if (publish && !wait)
        {
            throw new CliUsageException("--publish cannot be combined with --no-wait.");
        }

        var isDocumentCommand = documentsUpload || documentsDownload;
        if (isDocumentCommand && (containerId is not null || publish))
        {
            throw new CliUsageException("--container and --publish are not valid for document commands.");
        }
        if (outputPath is not null && !documentsDownload)
        {
            throw new CliUsageException("--out is only valid for 'documents download'.");
        }
        if (documentsDownload && recursive)
        {
            throw new CliUsageException("--recursive is not valid for 'documents download'.");
        }

        return new CliOptions(
            configure, emitMcpHeaders, showHelp, apiUrl, accountId, containerId,
            parallelism, timeout, wait, publish, recursive,
            documentsUpload, documentsDownload, outputPath, inputs);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CliUsageException($"{option} requires a value.");
        }

        return args[index++];
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
