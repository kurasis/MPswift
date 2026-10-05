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
                var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("Native manifest is empty.");
                if (manifest.SchemaVersion != 1 || manifest.Platform != "win-x64" || manifest.Libraries is null ||
                    manifest.Libraries.Select(l => l.Name).Distinct().Count() != manifest.Libraries.Length ||
                    new[] { "bass", "bassmix", "basswasapi" }.Any(name => manifest.Libraries.All(l => l.Name != name)))
                    throw new InvalidDataException("Unsupported native manifest.");

                // Validate every file before loading any executable code.
                foreach (var library in manifest.Libraries)
                {
                    if (library.Name is not ("bass" or "bassmix" or "basswasapi" or "bassflac" or "bassopus" or "bassalac" or "bass_aac") || library.FileName != library.Name + ".dll")
                        throw new InvalidDataException("Unexpected native library.");
                    var path = Path.Combine(AppContext.BaseDirectory, "native", "win-x64", library.FileName);
                    try
                    {
                        using var stream = File.OpenRead(path);
                        var actual = Convert.ToHexString(SHA256.HashData(stream));
                        if (!actual.Equals(library.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"SHA-256 mismatch: {library.FileName}. Re-provision approved files.");
                    }
                    catch (Exception error) when (library.IsDecoder && error is IOException or UnauthorizedAccessException or InvalidDataException)
                    { DecoderFileErrors[library.Name] = error.Message; }
                }
                foreach (var library in manifest.Libraries)
                    if (!library.IsDecoder)
                    if (!Handles.ContainsKey(library.Name))
                        Handles.Add(library.Name, NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "native", "win-x64", library.FileName)));
                _decoderPaths = manifest.Libraries.Where(l => l.IsDecoder && !DecoderFileErrors.ContainsKey(l.Name))
                    .Select(l => Path.Combine(AppContext.BaseDirectory, "native", "win-x64", l.FileName)).ToArray();
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

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var key = Path.GetFileNameWithoutExtension(name);
        return Handles.TryGetValue(key, out var handle) ? handle :
            throw new DllNotFoundException($"Unapproved native import: {name}.");
    }

    private sealed record Manifest(int SchemaVersion, string Platform, Library[] Libraries);
    private sealed record Library(string Name, string FileName, string Sha256, bool IsDecoder = false);
}
