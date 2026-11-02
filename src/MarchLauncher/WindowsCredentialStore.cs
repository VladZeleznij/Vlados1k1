using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MarchLauncher;

/// <summary>
/// Windows Credential Manager, current user's Generic credential vault.
/// Passwords never go into launcher-settings.json and are only stored after
/// the MTA server confirms a successful login/registration.
/// </summary>
internal static class WindowsCredentialStore
{
    private const string Target = "MARCH ROLE PLAY Launcher / march_auth";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    public static void Save(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Логин и пароль не могут быть пустыми.");

        // Credential Manager stores the blob encrypted by Windows for the current
        // user account. Never write passwords to JSON, logs or git.
        var bytes = Encoding.Unicode.GetBytes(password);
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = pointer,
                Persist = CredPersistLocalMachine,
                UserName = username
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Не удалось сохранить пароль в Windows.");
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
            if (pointer != IntPtr.Zero)
            {
                // Wipe unmanaged plaintext before freeing it.
                for (var i = 0; i < password.Length * 2; i++)
                    Marshal.WriteByte(pointer, i, 0);
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    public static string? Read(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        if (!CredRead(Target, CredTypeGeneric, 0, out var ptr))
        {
            var code = Marshal.GetLastWin32Error();
            if (code == ErrorNotFound) return null;
            throw new Win32Exception(code, "Не удалось прочитать сохранённый пароль.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(ptr);
            if (!string.Equals(credential.UserName, username, StringComparison.OrdinalIgnoreCase)
                || credential.CredentialBlob == IntPtr.Zero
                || credential.CredentialBlobSize == 0
                || (credential.CredentialBlobSize % 2) != 0
                || credential.CredentialBlobSize > 1024)
                return null;

            return Marshal.PtrToStringUni(
                credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally { CredFree(ptr); }
    }

    public static void Delete()
    {
        if (CredDelete(Target, CredTypeGeneric, 0)) return;
        var code = Marshal.GetLastWin32Error();
        if (code != ErrorNotFound)
            throw new Win32Exception(code, "Не удалось удалить сохранённый пароль.");
    }
}
