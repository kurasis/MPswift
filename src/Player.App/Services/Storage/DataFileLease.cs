using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Player.App.Services.Storage;

/// <summary>Pin application data names without following file links. Local directory links are resolved by the caller's directory lease.</summary>
internal sealed class DataFileLease : IDisposable
{
    public FileStream Stream { get; }
    public bool Created { get; }
    public readonly record struct FileIdentity(uint Volume, ulong Index, ulong CreatedAt);
    public FileIdentity? Identity => OperatingSystem.IsWindows() ? Identify(Stream.SafeFileHandle) : null;
    private DataFileLease(FileStream stream, bool created) { Stream = stream; Created = created; }
    public static DataFileLease? OpenExisting(string path, bool writableSharing = false) => Open(path, writableSharing, false);
    public static DataFileLease OpenOrCreate(string path) => Open(path, true, true)!;

    private static DataFileLease? Open(string path, bool writableSharing, bool create)
    {
        if (!OperatingSystem.IsWindows())
        {
            if ((File.Exists(path) || new FileInfo(path).LinkTarget is not null) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Application data files cannot use file links.");
            var share = writableSharing ? FileShare.ReadWrite : FileShare.Read;
            try { return new(new FileStream(path, FileMode.Open, FileAccess.Read, share), false); }
            catch (FileNotFoundException) when (create) { return new(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, share), true); }
            catch (FileNotFoundException) { return null; }
        }
        var handle = CreateFile(path, 0x80000000, writableSharing ? 3u : 1u, 0, 3, 0x00200000, 0);
        var created = false;
        if (handle.IsInvalid && create && Marshal.GetLastPInvokeError() == 2)
        {
            handle.Dispose();
            handle = CreateFile(path, 0x80000000, 3, 0, 1, 0x00200000, 0); created = true;
        }
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError(); handle.Dispose();
            if (!create && code == 2) return null;
            throw new IOException("Cannot pin application data file.", new Win32Exception(code));
        }
        try
        {
            Check(handle);
            return new(new FileStream(handle, FileAccess.Read), created);
        }
        catch { handle.Dispose(); throw; }
    }
    internal static void Check(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot inspect application data file.", new Win32Exception(Marshal.GetLastPInvokeError()));
        const uint unsupported = 0x10 | 0x400 | 0x1000 | 0x40000 | 0x400000;
        if ((info.Attributes & unsupported) != 0 || info.Links != 1)
            throw new IOException("Application data files must be regular local files without symbolic or hard links; originals preserved.");
    }
    internal static FileIdentity Identify(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot inspect owned data identity.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return new(info.VolumeSerial, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            ((ulong)(uint)info.Creation.dwHighDateTime << 32) | (uint)info.Creation.dwLowDateTime);
    }
    public static bool DeleteIfSame(string path, FileIdentity? identity)
    {
        if (!OperatingSystem.IsWindows()) { File.Delete(path); return true; }
        if (identity is null) throw new IOException("Owned data identity is required before cleanup.");
        using var handle = CreateFile(path, 0x80010000, 1, 0, 3, 0x00200000, 0);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            if (code == 2) return true;
            throw new IOException("Cannot open owned data for cleanup.", new Win32Exception(code));
        }
        if (Identify(handle) != identity) return false;
        var disposition = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(disposition, 1);
            if (!SetFileInformationByHandle(handle, 4, disposition, 1)) throw new IOException("Cannot remove owned data copy.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally { Marshal.FreeHGlobal(disposition); }
        return true;
    }
    public void Dispose() => Stream.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, nint information, uint size);
}
