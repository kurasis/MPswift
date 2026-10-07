using System.Runtime.InteropServices;
using System.Text;
using Player.App.Services.Audio;

namespace Player.AudioSmoke;

internal static class NativeFileSecurityValidation
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void MustRefuse(Action action, string message)
    { try { action(); } catch (IOException) { return; } catch (UnauthorizedAccessException) { return; } throw new InvalidOperationException(message); }
    public static void PrepareLinks(string fixture)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "lease-test"); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.wav"); File.Copy(fixture, source);
        // The lab creates links before disabling privileges; consuming an existing link needs no symlink-creation right.
        File.CreateSymbolicLink(Path.Combine(Environment.CurrentDirectory, "source-link.wav"), source);
        Directory.CreateSymbolicLink(Path.Combine(Environment.CurrentDirectory, "directory-link"), directory);
        File.CreateSymbolicLink(Path.Combine(Environment.CurrentDirectory, "remote-file-link.wav"), @"\\mpswift-test.invalid\owned\unavailable.wav");
        File.CreateSymbolicLink(Path.Combine(Environment.CurrentDirectory, "remote-file-chain.wav"), Path.Combine(Environment.CurrentDirectory, "remote-file-link.wav"));
        Directory.CreateSymbolicLink(Path.Combine(Environment.CurrentDirectory, "remote-directory-link"), @"\\mpswift-test.invalid\owned");
    }
    public static object Run(string fixture)
    {
        using (File.Open(Path.Combine(AppContext.BaseDirectory, "native", "win-x64", "bassflac.dll"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
        NativeLibraryBootstrap.LoadAndVerify();
        var delayed = NativeLibraryBootstrap.DecoderPaths;
        Check(delayed.Count == 10, "Not every approved decoder was pinned before loading.");
        foreach (var path in delayed)
            MustRefuse(() => { using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "Verified decoder remained writable before PluginLoad.");
        using (var context = new NativeDecodeContext()) Check(context.DecoderErrors.Count == 0, "Approved decoder preload failed.");
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "native", "win-x64"), "bass*.dll"))
        {
            var module = GetModuleHandle(Path.GetFileName(path)); Check(module != 0, "Approved module was not resident.");
            Check(string.Equals(ModulePath(module), path, StringComparison.OrdinalIgnoreCase), "Approved module resolved outside the owned tool copy.");
        }
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var name in new[] { "winmm.dll", "msacm32.dll", "shlwapi.dll", "msvcrt.dll" })
        {
            var module = GetModuleHandle(name); Check(module != 0, "Expected system dependency missing.");
            Check(string.Equals(ModulePath(module), Path.Combine(system, name), StringComparison.OrdinalIgnoreCase), "A system dependency was loaded from a planted directory.");
        }
        var directory = Path.Combine(Environment.CurrentDirectory, "lease-test");
        var source = Path.Combine(directory, "source.wav");
        var renamed = directory + "-renamed";
        using (var lease = LocalReadLease.Open(source))
        {
            MustRefuse(() => { using var writer = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "Read lease permitted source writes.");
            MustRefuse(() =>
            { try { File.Move(source, source + ".moved"); } finally { if (File.Exists(source + ".moved")) File.Move(source + ".moved", source); } }, "Read lease permitted source replacement.");
            MustRefuse(() =>
            { try { Directory.Move(directory, renamed); } finally { if (Directory.Exists(renamed)) Directory.Move(renamed, directory); } }, "Read lease permitted directory replacement.");
        }
        using (File.Open(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } // No writes: disposal must release the lease.
        var alias = Path.Combine(directory, "alias.wav");
        if (!CreateHardLink(alias, source, 0)) throw new IOException("Owned hardlink control could not be created.");
        MustRefuse(() => { using var native = LocalReadLease.Open(alias, executable: true); }, "Executable hardlink was accepted.");
        using (var lease = LocalReadLease.Open(source))
            MustRefuse(() => { using var writer = File.Open(alias, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }, "A hardlink alias bypassed source read sharing.");
        File.Delete(alias);
        var link = Path.Combine(Environment.CurrentDirectory, "source-link.wav");
        using (var lease = LocalReadLease.Open(link)) Check(string.Equals(lease.Path, source, StringComparison.OrdinalIgnoreCase), "Local media link did not resolve to its pinned target.");
        MustRefuse(() => { using var native = LocalReadLease.Open(link, executable: true); }, "Executable file link was accepted.");
        File.Delete(link);
        var directoryLink = Path.Combine(Environment.CurrentDirectory, "directory-link");
        using (var lease = LocalReadLease.Open(Path.Combine(directoryLink, "source.wav"))) Check(lease.Path == source, "Local directory link did not resolve to its pinned target.");
        MustRefuse(() => { using var native = LocalReadLease.Open(Path.Combine(directoryLink, "source.wav"), executable: true); }, "Executable directory link was accepted.");
        Directory.Delete(directoryLink);
        foreach (var path in new[] { "remote-file-link.wav", "remote-file-chain.wav", @"remote-directory-link\nested\unavailable.wav" })
        {
            try { LocalFileAccess.ValidateFile(Path.Combine(Environment.CurrentDirectory, path)); }
            catch (ArgumentException) { continue; } // LocalMediaPath must reject the target before a target metadata lookup.
            throw new InvalidOperationException("Remote link target was not rejected by local-path policy.");
        }
        return new { Status = "native-file-security-passed", DelayedDecoderPins = delayed.Count, DelayedDecoderWritesDenied = true,
            ApprovedModulePaths = true, SystemDependenciesFromSystem32 = true, InertCwdAndNativeDirectoryShadowsIgnored = true,
            SourceWritesAndReplacementsDenied = true, DirectoryReplacementDenied = true, HardlinkAliasWritesDenied = true,
            NativeLinksRejected = true, LocalMediaLinksPreserved = true, RemoteLinkChainsRejectedByPathPolicy = true, ReadLeaseReleased = true };
    }
    private static string ModulePath(nint module)
    { var path = new StringBuilder(32768); if (GetModuleFileName(module, path, path.Capacity) == 0) throw new IOException("Cannot inspect loaded module path."); return path.ToString(); }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(nint module, StringBuilder path, int size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string path, string target, nint security);
}
