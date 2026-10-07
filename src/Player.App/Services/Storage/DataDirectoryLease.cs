using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Player.Core.Media;

namespace Player.App.Services.Storage;

/// <summary>Keep Windows data paths stable through an operation, including local directory links.
/// Linux storage tests retain their original paths; this is not a portable filesystem sandbox.</summary>
internal sealed class DataDirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    public string DirectoryPath { get; private set; }
    private DataDirectoryLease(string path) => DirectoryPath = path;

    public static DataDirectoryLease Create(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) ValidateLocal(path);
        var ancestor = new DirectoryInfo(path);
        while (!ancestor.Exists) ancestor = ancestor.Parent ?? throw new IOException("No data directory parent exists.");
        using var parent = Open(ancestor.FullName);
        Directory.CreateDirectory(path);
        return Open(path); // Parent remains pinned until the new chain has been acquired.
    }

    public static DataDirectoryLease Open(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) ValidateLocal(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Data directory is unavailable.");
        var lease = new DataDirectoryLease(path);
        if (!OperatingSystem.IsWindows()) return lease;
        try
        {
            ValidateLocal(path);
            // Pin the requested objects as well as the physical chain: junctions cannot be retargeted by replacement.
            lease.PinChain(path, allowLinks: true);
            var physical = lease.Pin(path, followLinks: true);
            var resolved = Resolve(physical);
            ValidateLocal(resolved);
            lease.PinChain(resolved, allowLinks: false);
            if (!string.Equals(resolved, Resolve(physical), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Data directory changed while opening it.");
            lease.DirectoryPath = resolved;
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private void PinChain(string path, bool allowLinks)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            var handle = Pin(directory.FullName, followLinks: false);
            if (!GetFileInformationByHandle(handle, out var information)) throw Failure("Cannot inspect data directory");
            const uint unavailable = 0x1000 | 0x40000 | 0x400000;
            if ((information.Attributes & 0x10) == 0 || (information.Attributes & unavailable) != 0 ||
                !allowLinks && (information.Attributes & 0x400) != 0)
                throw new IOException("Data directory must be available locally.");
            if (allowLinks && (information.Attributes & 0x400) != 0)
            {
                // Reparse data can also be changed in place. Hold a share-read-only handle on the link itself.
                var link = CreateFile(directory.FullName, 0x80, 1, 0, 3, 0x02200000, 0);
                if (link.IsInvalid) { link.Dispose(); throw Failure("Cannot pin data directory link"); }
                _handles.Add(link);
            }
            if (!allowLinks && !string.Equals(directory.FullName.TrimEnd('\\'), Resolve(handle).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Data directory path changed while opening it.");
        }
    }
    private SafeFileHandle Pin(string path, bool followLinks)
    {
        // Read attributes, share read/write but never delete; open directory/reparse object.
        var handle = CreateFile(path, 0x80, 3, 0, 3, followLinks ? 0x02000000u : 0x02200000u, 0);
        if (handle.IsInvalid) { handle.Dispose(); throw Failure("Cannot pin data directory"); }
        _handles.Add(handle);
        return handle;
    }
    private static void ValidateLocal(string path)
    {
        LocalMediaPath.Parse(path.TrimEnd('\\') + "\\data-validation");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new IOException("Data directory must be on a local drive.");
    }
    private static string Resolve(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw Failure("Cannot resolve data directory");
        var path = buffer.ToString();
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("A local data directory is required.");
        return path[4..];
    }
    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
    public void Dispose() { foreach (var handle in _handles) handle.Dispose(); _handles.Clear(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
}
