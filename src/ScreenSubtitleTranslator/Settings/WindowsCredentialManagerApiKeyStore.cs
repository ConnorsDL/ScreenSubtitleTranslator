using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ScreenSubtitleTranslator.Settings;

public sealed class WindowsCredentialManagerApiKeyStore : IApiKeyStore
{
    public const string DefaultTargetName = "ScreenSubtitleTranslator:OpenAI";

    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxCredentialBlobSize = 2560;

    private readonly string _targetName;

    public WindowsCredentialManagerApiKeyStore(string targetName = DefaultTargetName)
    {
        _targetName = string.IsNullOrWhiteSpace(targetName)
            ? throw new ArgumentException("Credential target name cannot be empty.", nameof(targetName))
            : targetName;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CredRead(_targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw CreateCredentialException("read", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }

        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        if (bytes.Length > MaxCredentialBlobSize)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException("API key is too long for Windows Credential Manager.", nameof(apiKey));
        }

        var blobPointer = IntPtr.Zero;
        try
        {
            blobPointer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, blobPointer, bytes.Length);

            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = _targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = "OpenAI API Key"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw CreateCredentialException("save", Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blobPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(blobPointer);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CredDelete(_targetName, CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw CreateCredentialException("delete", error);
            }
        }

        return Task.CompletedTask;
    }

    private static InvalidOperationException CreateCredentialException(string operation, int error)
    {
        return new InvalidOperationException(
            $"Windows Credential Manager could not {operation} the OpenAI API key.",
            new Win32Exception(error));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
