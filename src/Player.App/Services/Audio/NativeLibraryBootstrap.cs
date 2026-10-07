using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;

namespace Player.App.Services.Audio;

/// <summary>Process-lifetime loader. Never searches PATH, working directory, or media directories.</summary>
public static class NativeLibraryBootstrap
{
    private static readonly Dictionary<string, nint> Handles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static readonly List<LocalReadLease> VerifiedFiles = [];
    private static bool _configured;
    private static string[] _decoderPaths = [];
    private static readonly Dictionary<string, string> DecoderFileErrors = [];
    public static IReadOnlyDictionary<string, string> DecoderValidationErrors => DecoderFileErrors;
    public static IReadOnlyList<string> DecoderPaths { get { LoadAndVerify(); return Array.AsReadOnly(_decoderPaths); } }

    public static IReadOnlyDictionary<string, string> LoadAndVerify()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Native validation requires a Windows x64 process.");

        lock (Gate)
        {
            if (!_configured)
            {
                var manifestPath = Path.Combine(AppContext.BaseDirectory, "native", "manifest.json");
                using var manifestRead = LocalReadLease.Open(manifestPath, executable: true);
                if (manifestRead.Stream.Length > 65536) throw new InvalidDataException("Native manifest exceeds its bound.");
                var manifest = JsonSerializer.Deserialize<Manifest>(manifestRead.Stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 })
                    ?? throw new InvalidDataException("Native manifest is empty.");
                if (manifest.SchemaVersion != 1 || manifest.Platform != "win-x64" || manifest.Libraries is null || manifest.Libraries.Length is < 3 or > 13 ||
                    manifest.Libraries.Any(l => l is null) || manifest.Libraries.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Libraries.Length ||
                    new[] { "bass", "bassmix", "basswasapi" }.Any(name => manifest.Libraries.All(l => l.Name != name)))
                    throw new InvalidDataException("Unsupported native manifest.");

                DecoderFileErrors.Clear();
                var pinStart = VerifiedFiles.Count;
                var verifiedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    // Validate every file before loading any executable code.
                    foreach (var library in manifest.Libraries)
                    {
                        if (library.Name is not ("bass" or "bassmix" or "basswasapi" or "bassflac" or "bassopus" or "bassalac" or "bass_aac" or "basswma" or "bassape" or "basswv" or "bassdsd" or "bass_mpc" or "bass_tta") || library.FileName != library.Name + ".dll" ||
                            library.IsDecoder != (library.Name is not ("bass" or "bassmix" or "basswasapi")) || library.Sha256 is null || library.Sha256.Length != 64)
                            throw new InvalidDataException("Unexpected native library.");
                        var path = Path.Combine(AppContext.BaseDirectory, "native", "win-x64", library.FileName);
                        try
                        {
                            var pinned = LocalReadLease.Open(path, executable: true);
                            try
                            {
                                var actual = Convert.ToHexString(SHA256.HashData(pinned.Stream));
                                if (!actual.Equals(library.Sha256, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException($"SHA-256 mismatch: {library.FileName}. Re-provision approved files.");
                                VerifiedFiles.Add(pinned); // Windows refuses writes/replacements until process exit, including decoder loads later.
                                verifiedPaths.Add(library.Name, pinned.Path);
                            }
                            catch { pinned.Dispose(); throw; }
                        }
                        catch (Exception error) when (library.IsDecoder && error is IOException or UnauthorizedAccessException or InvalidDataException)
                        { DecoderFileErrors[library.Name] = error.Message; }
                    }
                }
                catch
                {
                    foreach (var pinned in VerifiedFiles.Skip(pinStart)) pinned.Dispose();
                    VerifiedFiles.RemoveRange(pinStart, VerifiedFiles.Count - pinStart);
                    throw;
                }
                foreach (var library in manifest.Libraries)
                    if (!library.IsDecoder)
                    if (!Handles.ContainsKey(library.Name))
                        Handles.Add(library.Name, LoadRestricted(verifiedPaths[library.Name]));
                _decoderPaths = manifest.Libraries.Where(l => l.IsDecoder && !DecoderFileErrors.ContainsKey(l.Name))
                    .Select(l => verifiedPaths[l.Name]).ToArray();
                foreach (var assembly in new[] { typeof(Bass).Assembly, typeof(BassMix).Assembly, typeof(BassWasapi).Assembly })
                    NativeLibrary.SetDllImportResolver(assembly, Resolve);
                _configured = true;
            }

            return new Dictionary<string, string>
            {
                ["bass"] = Bass.Version.ToString(),
                ["bassmix"] = BassMix.Version.ToString(),
                ["basswasapi"] = BassWasapi.Version.ToString()
            };
        }
    }

    internal static void PrepareDecoder(string path)
    {
        lock (Gate)
        {
            if (!_configured || !_decoderPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new DllNotFoundException("Decoder path was not verified.");
            var key = Path.GetFileNameWithoutExtension(path);
            if (!Handles.ContainsKey(key)) Handles.Add(key, LoadRestricted(path));
        }
    }
    private static nint LoadRestricted(string path) => NativeLibrary.Load(path, typeof(NativeLibraryBootstrap).Assembly,
        DllImportSearchPath.System32); // BASS is already resident; external dependencies come only from System32.

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var key = Path.GetFileNameWithoutExtension(name);
        return Handles.TryGetValue(key, out var handle) ? handle :
            throw new DllNotFoundException($"Unapproved native import: {name}.");
    }

    private sealed record Manifest(int SchemaVersion, string Platform, Library[] Libraries);
    private sealed record Library(string Name, string FileName, string Sha256, bool IsDecoder = false);
}
