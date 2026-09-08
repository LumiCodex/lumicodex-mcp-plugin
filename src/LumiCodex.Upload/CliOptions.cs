namespace LumiCodex.Upload;

internal enum UploaderCommand
{
    Upload,
    Configure,
    McpHeaders,
    DocumentsUpload,
    DocumentsDownload,
    WhoAmI,
    AlbumsList,
    AlbumsCreate,
    EncodeUltra
}

internal sealed record CliOptions(
    UploaderCommand Command,
    bool ShowHelp,
    string? ApiUrl,
    string? AccountId,
    string? ContainerId,
    int Parallelism,
    TimeSpan ProcessingTimeout,
    bool WaitForProcessing,
    bool Publish,
    bool Recursive,
    string? OutputPath,
    string? Name,
    string? Access,
    string? Codec,
    string? VipsPath,
    string? CjxlPath,
    IReadOnlyList<string> Inputs,
    bool HasExplicitProcessingTimeout = false)
{
    internal bool Configure => Command == UploaderCommand.Configure;
    internal bool EmitMcpHeaders => Command == UploaderCommand.McpHeaders;
    internal bool DocumentsUpload => Command == UploaderCommand.DocumentsUpload;
    internal bool DocumentsDownload => Command == UploaderCommand.DocumentsDownload;

    internal static CliOptions Parse(string[] args)
    {
        var (command, index) = ParseCommand(args);
        var showHelp = false;
        string? apiUrl = null;
        string? accountId = null;
        string? containerId = null;
        string? outputPath = null;
        string? name = null;
        string? access = null;
        string? codec = null;
        string? vipsPath = null;
        string? cjxlPath = null;
        var parallelism = 4;
        var timeout = TimeSpan.FromMinutes(30);
        var explicitTimeout = false;
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
                case "--name":
                    name = ReadValue(args, ref index, argument);
                    break;
                case "--access":
                    access = ReadValue(args, ref index, argument).ToLowerInvariant();
                    break;
                case "--codec":
                    codec = ReadValue(args, ref index, argument).ToLowerInvariant();
                    break;
                case "--vips":
                    vipsPath = ReadValue(args, ref index, argument);
                    break;
                case "--cjxl":
                    cjxlPath = ReadValue(args, ref index, argument);
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
                        minutes is < 1 or > 3000)
                    {
                        throw new CliUsageException("--timeout-minutes must be between 1 and 3000.");
                    }
                    timeout = TimeSpan.FromMinutes(minutes);
                    explicitTimeout = true;
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

        Validate(command, containerId, outputPath, name, access, codec, vipsPath, cjxlPath,
            publish, recursive, wait, inputs);

        return new CliOptions(
            command, showHelp, apiUrl, accountId, containerId,
            parallelism, timeout, wait, publish, recursive, outputPath,
            name, access, codec, vipsPath, cjxlPath, inputs, explicitTimeout);
    }

    private static (UploaderCommand Command, int Index) ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return (UploaderCommand.Upload, 0);
        }

        return args[0].ToLowerInvariant() switch
        {
            "configure" => (UploaderCommand.Configure, 1),
            "mcp-headers" => (UploaderCommand.McpHeaders, 1),
            "whoami" => (UploaderCommand.WhoAmI, 1),
            "documents" => ParseSubcommand(args, "documents", "upload", "download",
                UploaderCommand.DocumentsUpload, UploaderCommand.DocumentsDownload),
            "albums" => ParseSubcommand(args, "albums", "list", "create",
                UploaderCommand.AlbumsList, UploaderCommand.AlbumsCreate),
            "encode" => args.Length > 1 && args[1].Equals("ultra", StringComparison.OrdinalIgnoreCase)
                ? (UploaderCommand.EncodeUltra, 2)
                : throw new CliUsageException("encode requires the subcommand 'ultra'."),
            _ => (UploaderCommand.Upload, 0)
        };
    }

    private static (UploaderCommand Command, int Index) ParseSubcommand(
        string[] args,
        string command,
        string firstName,
        string secondName,
        UploaderCommand firstCommand,
        UploaderCommand secondCommand)
    {
        if (args.Length < 2)
        {
            throw new CliUsageException(
                $"{command} requires a subcommand: '{firstName}' or '{secondName}'.");
        }

        if (args[1].Equals(firstName, StringComparison.OrdinalIgnoreCase))
        {
            return (firstCommand, 2);
        }
        if (args[1].Equals(secondName, StringComparison.OrdinalIgnoreCase))
        {
            return (secondCommand, 2);
        }

        throw new CliUsageException(
            $"{command} requires a subcommand: '{firstName}' or '{secondName}'.");
    }

    private static void Validate(
        UploaderCommand command,
        string? containerId,
        string? outputPath,
        string? name,
        string? access,
        string? codec,
        string? vipsPath,
        string? cjxlPath,
        bool publish,
        bool recursive,
        bool wait,
        IReadOnlyList<string> inputs)
    {
        if (publish && !wait)
        {
            throw new CliUsageException("--publish cannot be combined with --no-wait.");
        }

        if (command is UploaderCommand.DocumentsUpload or UploaderCommand.DocumentsDownload &&
            (containerId is not null || publish))
        {
            throw new CliUsageException("--container and --publish are not valid for document commands.");
        }
        if (outputPath is not null && command is not (UploaderCommand.DocumentsDownload or UploaderCommand.EncodeUltra))
        {
            throw new CliUsageException("--out is valid only for document download and ultra encoding.");
        }
        if (command == UploaderCommand.DocumentsDownload && recursive)
        {
            throw new CliUsageException("--recursive is not valid for 'documents download'.");
        }

        var albumCommand = command is UploaderCommand.AlbumsList or UploaderCommand.AlbumsCreate or UploaderCommand.WhoAmI;
        if (albumCommand && (containerId is not null || publish || recursive || !wait || inputs.Count > 0))
        {
            throw new CliUsageException("Account and album commands accept only --api-url, --account, --name, and --access as applicable.");
        }
        if (command == UploaderCommand.AlbumsCreate)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CliUsageException("albums create requires --name.");
            }
            if (access is not ("public" or "private"))
            {
                throw new CliUsageException("albums create requires --access public|private.");
            }
        }
        else if (name is not null || access is not null)
        {
            throw new CliUsageException("--name and --access are valid only for 'albums create'.");
        }

        if (command == UploaderCommand.EncodeUltra)
        {
            if (codec is not ("avif" or "jxl"))
            {
                throw new CliUsageException("encode ultra requires --codec avif|jxl.");
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new CliUsageException("encode ultra requires --out DIR.");
            }
            if (inputs.Count == 0)
            {
                throw new CliUsageException("encode ultra requires at least one input image.");
            }
        }
        else if (codec is not null || vipsPath is not null || cjxlPath is not null)
        {
            throw new CliUsageException("--codec, --vips, and --cjxl are valid only for 'encode ultra'.");
        }
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
