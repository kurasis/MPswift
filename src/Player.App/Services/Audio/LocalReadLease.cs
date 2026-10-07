using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Player.App.Services.Audio;

/// <summary>Pin a validated read and its resolved directory chain while a path-based parser opens it.</summary>
internal sealed class LocalReadLease : IDisposable
{
    private readonly List<SafeFileHandle> _directories = [];
    public FileStream Stream { get; }
    public string Path { get; }

    private LocalReadLease(FileStream stream, string path) { Stream = stream; Path = path; }

    public static LocalReadLease Open(string path, bool executable = false)
    {
        path = System.IO.Path.GetFullPath(path);
        if (executable)
            for (var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Native library directories cannot use reparse points.");
        var validated = LocalFileAccess.ValidateFile(path);
        if (executable && !string.Equals(path, validated, StringComparison.OrdinalIgnoreCase)) throw new IOException("Native libraries cannot use file links.");
        path = validated;
        var handle = CreateFile(path, 0x80000000, 1, 0, 3, 0x00200000, 0); // Read, share-read only, open reparse object.
        if (handle.IsInvalid) { handle.Dispose(); throw Failure("Cannot pin local file"); }
        LocalReadLease? lease = null;
        try
        {
            Check(handle, false, executable);
            var resolved = Resolve(handle);
            LocalFileAccess.ValidateFile(resolved);
            lease = new(new FileStream(handle, FileAccess.Read), resolved);
            for (var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(resolved)!); directory is not null; directory = directory.Parent)
            {
                var pinned = CreateFile(directory.FullName, 0x80, 3, 0, 3, 0x02200000, 0); // Read attributes, no delete sharing/link following.
                if (pinned.IsInvalid) { pinned.Dispose(); throw Failure("Cannot pin local directory"); }
                lease._directories.Add(pinned);
                Check(pinned, true, false);
                if (!string.Equals(directory.FullName.TrimEnd('\\'), Resolve(pinned).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Local directory changed while opening a file.");
            }
            if (!string.Equals(resolved, Resolve(handle), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Local file changed while pinning its directory chain.");
            return lease;
        }
        catch { if (lease is not null) lease.Dispose(); else handle.Dispose(); throw; }
    }

    private static void Check(SafeFileHandle handle, bool directory, bool executable)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw Failure("Cannot inspect pinned file");
        const uint unavailable = 0x400 | 0x1000 | 0x40000 | 0x400000; // Reparse, offline, recall-on-open/data.
        if ((info.Attributes & unavailable) != 0 || ((info.Attributes & 0x10) != 0) != directory || executable && info.Links != 1)
            throw new IOException("Pinned file must be available locally without reparse points; native libraries cannot be hardlinked.");
    }
    private static string Resolve(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw Failure("Cannot resolve pinned file");
        var path = buffer.ToString();
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("A local DOS path is required for pinned reads.");
        return path[4..];
    }
    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
    public void Dispose() { Stream.Dispose(); foreach (var directory in _directories) directory.Dispose(); _directories.Clear(); }

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
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
}
