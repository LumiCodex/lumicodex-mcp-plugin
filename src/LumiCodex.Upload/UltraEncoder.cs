using System.Diagnostics;

namespace LumiCodex.Upload;

internal static class UltraEncoder
{
    private const string DisplayP3ProfileBase64 =
        "AAAB4GxjbXMEIAAAbW50clJHQiBYWVogB+IAAwAUAAkADgAdYWNzcE1TRlQAAAAAc2F3c2N0cmwAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1oYW5kwzc6zlf4VsuhS9h6V6sQYQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKZGVzYwAAAPwAAAAiY3BydAAAASAAAAAid3RwdAAAAUQAAAAUY2hhZAAAAVgAAAAsclhZWgAAAYQAAAAUZ1hZWgAAAZgAAAAUYlhZWgAAAawAAAAUclRSQwAAAcAAAAAgZ1RSQwAAAcAAAAAgYlRSQwAAAcAAAAAgbWx1YwAAAAAAAAABAAAADGVuVVMAAAAGAAAAHABzAFAAMwAAbWx1YwAAAAAAAAABAAAADGVuVVMAAAAGAAAAHABDAEMAMAAAWFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDEIAAAXe///zJQAAB5MAAP2Q///7of///aIAAAPcAADAblhZWiAAAAAAAACD3wAAPb////+7WFlaIAAAAAAAAEq/AACxNwAACrlYWVogAAAAAAAAKDgAABEKAADIuXBhcmEAAAAAAAMAAAACZmkAAPKnAAANWQAAE9AAAApb";

    internal static async Task<IReadOnlyList<UltraEncodeResult>> EncodeAsync(
        CliOptions options,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetFullPath(options.OutputPath!);
        Directory.CreateDirectory(outputDirectory);
        var codec = options.Codec!;
        var vips = ResolveVipsPath(options.VipsPath);
        var cjxl = ResolveCjxlPath(options.CjxlPath);
        var results = new List<UltraEncodeResult>(options.Inputs.Count);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"lumicodex-ultra-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var profilePath = Path.Combine(tempDirectory, "DisplayP3-v4.icc");
            await File.WriteAllBytesAsync(
                profilePath,
                Convert.FromBase64String(DisplayP3ProfileBase64),
                cancellationToken);

            foreach (var rawInput in options.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = Path.GetFullPath(rawInput);
                if (!File.Exists(input))
                {
                    throw new CliUsageException($"Input path not found: {input}");
                }

                var workingTiff = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tif");
                await RunProcessAsync(
                    vips,
                    BuildColorTransformArguments(input, workingTiff, profilePath),
                    "converting the Lightroom render to Display P3",
                    "libvips is required for Ultra exports. Install it or pass --vips PATH.",
                    cancellationToken);

                var output = ReserveOutputPath(outputDirectory, Path.GetFileNameWithoutExtension(input), codec);
                try
                {
                    if (codec == "avif")
                    {
                        await RunProcessAsync(
                            vips,
                            BuildAvifArguments(workingTiff, output, profilePath),
                            "encoding AVIF",
                            "A libvips build with AVIF support is required. Install it or pass --vips PATH.",
                            cancellationToken);
                    }
                    else
                    {
                        var workingPng = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.png");
                        await RunProcessAsync(
                            vips,
                            BuildPngArguments(workingTiff, workingPng, profilePath),
                            "preparing the 16-bit JPEG XL input",
                            "libvips is required for Ultra exports. Install it or pass --vips PATH.",
                            cancellationToken);
                        await RunProcessAsync(
                            cjxl,
                            BuildJxlArguments(workingPng, output),
                            "encoding JPEG XL",
                            "cjxl with --override_bitdepth support is required for 10-bit JPEG XL. Install libjxl-tools or pass --cjxl PATH.",
                            cancellationToken);
                    }
                }
                catch
                {
                    File.Delete(output);
                    throw;
                }

                results.Add(new UltraEncodeResult(
                    input,
                    output,
                    codec,
                    10,
                    "Display P3"));
            }

            return results;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; conversion output lives outside this directory.
            }
        }
    }

    internal static IReadOnlyList<string> BuildColorTransformArguments(
        string input,
        string output,
        string profilePath) =>
    [
        "icc_transform", input, output, profilePath,
        "--embedded",
        "--intent", "relative",
        "--black-point-compensation",
        "--depth", "16"
    ];

    internal static IReadOnlyList<string> BuildAvifArguments(
        string input,
        string output,
        string profilePath) =>
    [
        "heifsave", input, output,
        "--Q", "80",
        "--bitdepth", "10",
        "--compression", "av1",
        "--effort", "4",
        "--subsample-mode", "off",
        "--keep", "all",
        "--profile", profilePath
    ];

    internal static IReadOnlyList<string> BuildPngArguments(
        string input,
        string output,
        string profilePath) =>
    [
        "pngsave", input, output,
        "--bitdepth", "16",
        "--keep", "all",
        "--profile", profilePath
    ];

    internal static IReadOnlyList<string> BuildJxlArguments(string input, string output) =>
    [
        input,
        output,
        "--distance=0.7",
        "--effort=7",
        "--override_bitdepth=10"
    ];

    private static string ResolveVipsPath(string? option)
    {
        var path = option;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetEnvironmentVariable("LUMICODEX_VIPS_PATH");
        }
        return string.IsNullOrWhiteSpace(path) ? "vips" : path;
    }

    private static string ResolveCjxlPath(string? option)
    {
        var path = option;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetEnvironmentVariable("LUMICODEX_CJXL_PATH");
        }
        return string.IsNullOrWhiteSpace(path) ? "cjxl" : path;
    }

    private static string ReserveOutputPath(string directory, string baseName, string extension)
    {
        for (var suffix = 0; ; suffix++)
        {
            var name = suffix == 0
                ? $"{baseName}.{extension}"
                : $"{baseName}-{suffix}.{extension}";
            var candidate = Path.Combine(directory, name);
            try
            {
                using var _ = new FileStream(candidate, FileMode.CreateNew);
                return candidate;
            }
            catch (IOException)
            {
                // Preserve existing exports and choose the next deterministic suffix.
            }
        }
    }

    private static async Task RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string operation,
        string missingExecutableMessage,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"{missingExecutableMessage} ({exception.Message})",
                exception);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executable)} failed while {operation}: {detail.Trim()}");
            }
        }
    }
}
