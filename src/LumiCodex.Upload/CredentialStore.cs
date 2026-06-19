using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LumiCodex.Upload;

internal interface ICredentialStore
{
    Task<string?> ReadAsync(Uri apiUrl, CancellationToken cancellationToken);
    Task WriteAsync(Uri apiUrl, string apiKey, CancellationToken cancellationToken);
}

internal static class CredentialStore
{
    internal static ICredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacCredentialStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxCredentialStore();
        }

        return new UnsupportedCredentialStore();
    }

    internal static string Target(Uri apiUrl)
        => $"LumiCodex.Upload:{apiUrl.GetLeftPart(UriPartial.Authority)}";
}

internal sealed class UnsupportedCredentialStore : ICredentialStore
{
    public Task<string?> ReadAsync(Uri apiUrl, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task WriteAsync(Uri apiUrl, string apiKey, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(
            "Secure credential storage is not supported on this operating system. Use LUMICODEX_API_KEY.");
}

internal sealed class LinuxCredentialStore : ICredentialStore
{
    public async Task<string?> ReadAsync(Uri apiUrl, CancellationToken cancellationToken)
    {
        var result = await CredentialProcess.RunAsync(
            "secret-tool",
            ["lookup", "service", "lumicodex-upload", "endpoint", CredentialStore.Target(apiUrl)],
            null,
            cancellationToken);
        return result.ExitCode == 0 ? EmptyToNull(result.StandardOutput) : null;
    }

    public async Task WriteAsync(Uri apiUrl, string apiKey, CancellationToken cancellationToken)
    {
        var result = await CredentialProcess.RunAsync(
            "secret-tool",
            [
                "store",
                "--label=LumiCodex Upload API key",
                "service", "lumicodex-upload",
                "endpoint", CredentialStore.Target(apiUrl)
            ],
            apiKey,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Linux Secret Service rejected the credential: {result.StandardError}");
        }
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class MacCredentialStore : ICredentialStore
{
    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public Task<string?> ReadAsync(Uri apiUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = NativeUtf8.Create(CredentialStore.Target(apiUrl));
        using var account = NativeUtf8.Create("lumicodex-upload");

        var status = SecKeychainFindGenericPassword(
            0,
            service.Length,
            service.Pointer,
            account.Length,
            account.Pointer,
            out var passwordLength,
            out var passwordData,
            out var item);

        if (status == ItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }
        if (status != Success)
        {
            throw new InvalidOperationException($"macOS Keychain lookup failed with status {status}.");
        }

        try
        {
            var bytes = new byte[passwordLength];
            if (passwordLength > 0)
            {
                Marshal.Copy(passwordData, bytes, 0, checked((int)passwordLength));
            }
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            SecKeychainItemFreeContent(0, passwordData);
            if (item != 0)
            {
                CFRelease(item);
            }
        }
    }

    public Task WriteAsync(Uri apiUrl, string apiKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = NativeUtf8.Create(CredentialStore.Target(apiUrl));
        using var account = NativeUtf8.Create("lumicodex-upload");
        using var password = NativeUtf8.Create(apiKey, clearOnDispose: true);

        var findStatus = SecKeychainFindGenericPassword(
            0,
            service.Length,
            service.Pointer,
            account.Length,
            account.Pointer,
            out _,
            out var existingPassword,
            out var item);
        if (findStatus == Success)
        {
            SecKeychainItemFreeContent(0, existingPassword);
            try
            {
                var updateStatus = SecKeychainItemModifyAttributesAndData(
                    item, 0, password.Length, password.Pointer);
                if (updateStatus != Success)
                {
                    throw new InvalidOperationException(
                        $"macOS Keychain update failed with status {updateStatus}.");
                }
            }
            finally
            {
                if (item != 0)
                {
                    CFRelease(item);
                }
            }
            return Task.CompletedTask;
        }
        if (findStatus != ItemNotFound)
        {
            throw new InvalidOperationException(
                $"macOS Keychain lookup failed with status {findStatus}.");
        }

        var addStatus = SecKeychainAddGenericPassword(
            0,
            service.Length,
            service.Pointer,
            account.Length,
            account.Pointer,
            password.Length,
            password.Pointer,
            out var addedItem);
        if (addedItem != 0)
        {
            CFRelease(addedItem);
        }
        if (addStatus != Success)
        {
            throw new InvalidOperationException(
                $"macOS Keychain write failed with status {addStatus}.");
        }

        return Task.CompletedTask;
    }

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        nint keychainOrArray,
        uint serviceNameLength,
        nint serviceName,
        uint accountNameLength,
        nint accountName,
        out uint passwordLength,
        out nint passwordData,
        out nint itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        nint keychain,
        uint serviceNameLength,
        nint serviceName,
        uint accountNameLength,
        nint accountName,
        uint passwordLength,
        nint passwordData,
        out nint itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemModifyAttributesAndData(
        nint itemRef,
        nint attributes,
        uint dataLength,
        nint data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(nint attributes, nint data);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(nint value);
}

internal sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;

    public Task<string?> ReadAsync(Uri apiUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(CredentialStore.Target(apiUrl), GenericCredential, 0, out var pointer))
        {
            const int notFound = 1168;
            var error = Marshal.GetLastWin32Error();
            if (error == notFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var secret = credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    checked((int)credential.CredentialBlobSize / 2));
            return Task.FromResult(secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task WriteAsync(Uri apiUrl, string apiKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var secretPointer = Marshal.StringToCoTaskMemUni(apiKey);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = CredentialStore.Target(apiUrl),
                CredentialBlobSize = checked((uint)Encoding.Unicode.GetByteCount(apiKey)),
                CredentialBlob = secretPointer,
                Persist = PersistLocalMachine,
                UserName = "lumicodex-upload"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(secretPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out nint credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", SetLastError = false)]
    private static extern void CredFree(nint credentialPointer);
}

internal static class CredentialProcess
{
    internal static async Task<CredentialProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

            if (standardInput is not null)
            {
                // Write the raw secret with no trailing newline so the stored value matches
                // exactly what macOS/Windows persist. secret-tool reads stdin until EOF, which
                // the following Close() signals.
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new CredentialProcessResult(
                process.ExitCode,
                await outputTask,
                (await errorTask).Trim());
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"'{fileName}' is required for secure credential storage but was not found.",
                exception);
        }
    }
}

internal sealed record CredentialProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class NativeUtf8 : IDisposable
{
    private readonly bool clearOnDispose;

    private NativeUtf8(nint pointer, uint length, bool clearOnDispose)
    {
        Pointer = pointer;
        Length = length;
        this.clearOnDispose = clearOnDispose;
    }

    internal nint Pointer { get; }
    internal uint Length { get; }

    internal static NativeUtf8 Create(string value, bool clearOnDispose = false)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        if (bytes.Length > 0)
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        if (clearOnDispose)
        {
            Array.Clear(bytes);
        }
        return new NativeUtf8(pointer, checked((uint)bytes.Length), clearOnDispose);
    }

    public void Dispose()
    {
        if (clearOnDispose && Length > 0)
        {
            var zeros = new byte[Length];
            Marshal.Copy(zeros, 0, Pointer, zeros.Length);
        }
        Marshal.FreeHGlobal(Pointer);
    }
}
