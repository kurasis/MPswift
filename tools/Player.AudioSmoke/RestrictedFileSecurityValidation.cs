using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace Player.AudioSmoke;

internal static class RestrictedFileSecurityValidation
{
    public static object Run(string fixture)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using (var identity = WindowsIdentity.GetCurrent())
        {
            // Only the fresh token-owned lab root: make the current user's access explicit, independent of Admin membership.
            var directory = new DirectoryInfo(Environment.CurrentDirectory); var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }
        NativeFileSecurityValidation.PrepareLinks(fixture);
        if (!OpenProcessToken(GetCurrentProcess(), 2 | 8, out var original)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        using (original)
        {
            var sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
            var memory = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, memory, bytes.Length); var disabled = new SidAndAttributes { Sid = memory };
                if (!CreateRestrictedToken(original, 1, 1, ref disabled, 0, 0, 0, 0, out var restricted)) throw new Win32Exception(Marshal.GetLastPInvokeError());
                using (restricted)
                    return WindowsIdentity.RunImpersonated(restricted, () =>
                    {
                        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                        using var identity = WindowsIdentity.GetCurrent();
                        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("File test still has an enabled Administrator group.");
                        return new { Status = "restricted-file-security-passed", AdministratorGroupDisabled = true, MaximumPrivilegesDisabled = true,
                            Scope = "Synchronous filesystem/loader operations under an impersonated restricted token; not a process sandbox", Checks = NativeFileSecurityValidation.Run(fixture) };
                    });
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public nint Sid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(SafeAccessTokenHandle existing, uint flags, uint disableCount, ref SidAndAttributes disable,
        uint deleteCount, nint delete, uint restrictCount, nint restrict, out SafeAccessTokenHandle token);
}
