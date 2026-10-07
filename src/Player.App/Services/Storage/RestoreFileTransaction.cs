using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Player.App.Services.Storage;

/// <summary>Install owned copies and retain originals. Windows mutations address the held objects, not reopened names.</summary>
internal static class RestoreFileTransaction
{
    public static void PublishNew(string destination, Action<Stream> write)
    {
        var file = HeldFile.Create(destination + ".partial-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        try { write(file.Stream); file.Stream.Flush(true); file.MoveTo(destination); file.Commit(); }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { file.Dispose(); }
            catch (Exception cleanup) when (failure is not null)
            { throw new AggregateException("Archive writing and owned-file cleanup failed.", failure, cleanup); }
        }
    }
    public static void Install((string Target, Stream Source)[] replacements,
        (string Original, string Preserved, bool DiscardOnSuccess)[] originals, Action<int>? afterInstall = null)
    {
        var prepared = new List<(string Target, HeldFile File)>();
        var retained = new List<(string Original, string Preserved, bool DiscardOnSuccess, HeldFile File)>();
        var moved = new List<(string Original, HeldFile File)>();
        var installed = new List<HeldFile>();
        var errors = new List<Exception>();
        try
        {
            // Acquire every original before changing any name. A locked original aborts without moving others.
            foreach (var item in originals)
                if (HeldFile.OpenOriginal(item.Original) is { } file) retained.Add((item.Original, item.Preserved, item.DiscardOnSuccess, file));
            foreach (var item in replacements)
            {
                var copy = HeldFile.Create(item.Target + ".restore-" + Guid.NewGuid().ToString("N"));
                prepared.Add((item.Target, copy));
                item.Source.Position = 0; item.Source.CopyTo(copy.Stream); copy.Stream.Flush(true);
            }
            foreach (var item in retained)
            {
                item.File.MoveTo(item.Preserved); moved.Add((item.Original, item.File));
            }
            foreach (var item in prepared)
            {
                item.File.MoveTo(item.Target); installed.Add(item.File); afterInstall?.Invoke(installed.Count);
            }
            foreach (var item in prepared) item.File.Commit();
        }
        catch (Exception failure)
        {
            errors.Add(failure);
            foreach (var file in installed.AsEnumerable().Reverse())
                try { file.DeleteOwned(); } catch (Exception error) { errors.Add(error); }
            foreach (var item in moved.AsEnumerable().Reverse())
                try { item.File.MoveTo(item.Original); } catch (Exception error) { errors.Add(error); }
        }
        finally
        {
            if (errors.Count == 0)
                foreach (var item in retained.Where(item => item.DiscardOnSuccess))
                    try { item.File.DeleteHeld(); } catch (Exception error) { errors.Add(error); }
            foreach (var item in prepared)
                try { item.File.Dispose(); } catch (Exception error) { errors.Add(error); }
            foreach (var item in retained)
                try { item.File.Dispose(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 1) throw new AggregateException("Restore interrupted; retained originals require review before reopening.", errors);
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
    }

    private sealed class HeldFile : IDisposable
    {
        private string _path;
        private bool _owned;
        private bool _closed;
        public FileStream Stream { get; }
        private HeldFile(string path, FileStream stream, bool owned) { _path = path; Stream = stream; _owned = owned; }

        public static HeldFile Create(string path)
        {
            if (!OperatingSystem.IsWindows()) return new(path, new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read), true);
            var handle = CreateFile(path, 0xC0010000, 1, 0, 1, 0x00200000, 0); // Read/write/delete, share-read, CreateNew, no link following.
            if (handle.IsInvalid) { handle.Dispose(); throw Failure("Cannot reserve restore file"); }
            try { return new(path, new FileStream(handle, FileAccess.ReadWrite), true); }
            catch { handle.Dispose(); throw; }
        }
        public static HeldFile? OpenOriginal(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                if (Directory.Exists(path)) throw new IOException("A data filename is occupied by a directory; originals preserved.");
                if (new FileInfo(path).LinkTarget is not null) throw new IOException("Application data files cannot use file links.");
                try { return new(path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), false); }
                catch (FileNotFoundException) { return null; }
            }
            var handle = CreateFile(path, 0x80010000, 1, 0, 3, 0x00200000, 0); // Hold the original entry, including a file link, for same-handle rename.
            if (handle.IsInvalid)
            {
                var code = Marshal.GetLastPInvokeError(); handle.Dispose();
                if (code == 2) return null;
                throw new IOException("Cannot retain original restore file; originals preserved.", new Win32Exception(code));
            }
            try { DataFileLease.Check(handle); return new(path, new FileStream(handle, FileAccess.Read), false); }
            catch { handle.Dispose(); throw; }
        }
        public void MoveTo(string destination)
        {
            if (OperatingSystem.IsWindows())
            {
                var bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(destination));
                // FILE_RENAME_INFO on the supported x64 Windows target: BOOLEAN, padding, HANDLE, DWORD, WCHAR[].
                // Win32 converts the DOS destination to an NT path as a NUL-terminated string.
                // FileNameLength excludes the terminator; keep it inside the allocated input buffer.
                var size = checked(20 + bytes.Length + sizeof(char));
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt64(buffer, 0, 0); Marshal.WriteIntPtr(buffer, 8, 0);
                    Marshal.WriteInt32(buffer, 16, bytes.Length); Marshal.Copy(bytes, 0, buffer + 20, bytes.Length);
                    Marshal.WriteInt16(buffer, 20 + bytes.Length, 0);
                    if (!SetFileInformationByHandle(Stream.SafeFileHandle, 3, buffer, checked((uint)size)))
                        throw Failure("Cannot rename held restore file");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            else
            {
                // Linux managed tests cover transaction behavior, not Windows handle identity guarantees.
                Stream.Dispose(); _closed = true; File.Move(_path, destination);
            }
            _path = destination;
        }
        public void Commit() => _owned = false;
        public void DeleteOwned()
        {
            if (!_owned) return;
            DeleteHeld();
        }
        public void DeleteHeld()
        {
            if (_closed && OperatingSystem.IsWindows()) return;
            if (OperatingSystem.IsWindows())
            {
                var disposition = Marshal.AllocHGlobal(1);
                try
                {
                    Marshal.WriteByte(disposition, 1);
                    if (!SetFileInformationByHandle(Stream.SafeFileHandle, 4, disposition, 1)) throw Failure("Cannot remove owned restore file");
                }
                finally { Marshal.FreeHGlobal(disposition); }
                Stream.Dispose(); _closed = true; // Complete delete before trying to restore an original name.
            }
            else { Stream.Dispose(); _closed = true; File.Delete(_path); }
            _owned = false;
        }
        public void Dispose()
        {
            try { DeleteOwned(); }
            finally { if (!_closed) { Stream.Dispose(); _closed = true; } }
        }
    }
    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, nint information, uint size);
}
