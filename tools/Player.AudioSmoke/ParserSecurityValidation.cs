using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Player.App.Services.Audio;

namespace Player.AudioSmoke;

internal static class ParserSecurityValidation
{
    private const string Prefix = "mpswift-parser-security-";
    private const string Marker = ".player-parser-validation";
    private sealed record Request(string Kind, string Source);
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 4 * 1024 * 1024 + 16) throw new InvalidDataException("Security seed/input exceeds its bound.");
        return Convert.ToHexString(SHA256.HashData(file));
    }

    public static async Task<object> RunAsync(string fixtures)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" && Environment.GetEnvironmentVariable("MPSWIFT_ISOLATED_SECURITY_LAB") != "1")
            throw new InvalidOperationException("Parser security testing requires disposable CI or an explicitly configured isolated lab.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the security suite through the provisioned absolute dotnet host.");
        fixtures = Path.GetFullPath(fixtures); LocalFileAccess.ValidateDirectory(fixtures);
        var token = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", Prefix + token);
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, Marker), token);
        var results = new List<object>(); var failures = new List<string>();
        var sources = new[] { "audio/pcm16.wav", "audio/pcm16.aiff", "audio/flac16.flac", "audio/mp3-vbr.mp3", "audio/vorbis.ogg", "audio/opus.opus",
            "audio/alac.m4a", "audio/aac-lc.aac", "audio/wma2.wma", "audio/wavpack.wv", "audio/tta.tta", "audio/dsd64.dsf", "audio-extended/monkey.ape", "audio-extended/musepack.mpc" };
        var hashes = sources.Concat(new[] { "audio-extended/hybrid-corrected.wv", "audio-extended/hybrid-corrected.wvc" }).ToDictionary(name => name, name => Hash(Path.Combine(fixtures, name)));
        object? nativeFiles = null;
        try
        {
            var toolRoot = Path.Combine(root, "tool");
            CopyDirectory(AppContext.BaseDirectory, toolRoot);
            var tool = Path.Combine(toolRoot, "Player.AudioSmoke.dll");
            // Inert bytes only; never execute a planted test DLL. Restricted imports must ignore these locations.
            foreach (var directory in new[] { root, Path.Combine(toolRoot, "native", "win-x64") })
                foreach (var name in new[] { "winmm.dll", "msacm32.dll", "shlwapi.dll", "msvcrt.dll" }) File.WriteAllText(Path.Combine(directory, name), "owned inert DLL shadow");
            var leaseSource = Path.Combine(root, "lease-control.wav"); File.Copy(Path.Combine(fixtures, sources[0]), leaseSource);
            foreach (var (name, destination) in new[] { ("hybrid-corrected.wv", "lifetime.wv"), ("hybrid-corrected.wvc", "lifetime.wvc"), ("source-pcm16.wav", "lifetime-reference.wav") })
                File.Copy(Path.Combine(fixtures, "audio-extended", name), Path.Combine(root, destination));
            var requestPath = Path.Combine(root, "request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new Request("timeout-control", leaseSource)));
            try
            {
                await SecurityChildProcess.RunAsync(tool, root, requestPath, TimeSpan.FromSeconds(2));
                throw new InvalidOperationException("Waiting worker was not stopped by its owned deadline.");
            }
            catch (OperationCanceledException) { } // RunAsync must kill and wait for the exact owned worker before returning.
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new Request("lease", leaseSource)));
            var lease = await SecurityChildProcess.RunAsync(tool, root, requestPath);
            if (lease.ExitCode != 0) throw new InvalidOperationException("Native file/loader worker failed: " + lease.Output + lease.Errors);
            using (var json = JsonDocument.Parse(lease.Output))
            {
                if (json.RootElement.GetProperty("Status").GetString() != "restricted-file-security-passed") throw new InvalidDataException("Restricted loader evidence missing.");
                nativeFiles = json.RootElement.Clone();
            }
            foreach (var name in sources)
            {
                var source = File.ReadAllBytes(Path.Combine(fixtures, name));
                if (source.Length is < 128 or > 4 * 1024 * 1024) throw new InvalidDataException("Security seeds must be small synthetic fixtures.");
                var mutations = new List<string> { "control", "truncated", "header-bits", "declared-size", "body-bits", "body-truncated", "tail-bits", "lifecycle-control" };
                if (Path.GetExtension(name) is ".ogg" or ".opus") mutations.Add("late-stream");
                foreach (var mutation in mutations)
                foreach (var kind in mutation == "lifecycle-control" ? new[] { "lifecycle" } : mutation == "late-stream" ? new[] { "metadata" } : new[] { "decode", "metadata" })
                {
                    var bytes = Mutate(source, Path.GetExtension(name), mutation);
                    var path = Path.Combine(root, "case-" + results.Count + Path.GetExtension(name)); File.WriteAllBytes(path, bytes);
                    var before = Hash(path); File.WriteAllText(requestPath, JsonSerializer.Serialize(new Request(kind, path)));
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        var child = await SecurityChildProcess.RunAsync(tool, root, requestPath);
                        if (child.ExitCode != 0) throw new InvalidOperationException($"Worker exit {child.ExitCode}: {child.Output} {child.Errors}");
                        using var json = JsonDocument.Parse(child.Output); var evidence = json.RootElement;
                        var outcome = evidence.GetProperty("Outcome").GetString();
                        if (evidence.GetProperty("Status").GetString() != "bounded-parser-worker-completed" || outcome is not ("decoded" or "metadata-parsed" or "lifecycle-passed" or "rejected") ||
                            kind == "decode" && mutation == "control" && outcome != "decoded" ||
                            kind == "lifecycle" && outcome != "lifecycle-passed" ||
                            kind == "metadata" && mutation == "control" && name.EndsWith(".wav", StringComparison.Ordinal) && outcome != "metadata-parsed")
                            throw new InvalidDataException("Worker did not produce current valid control/rejection evidence.");
                        if (mutation == "late-stream" && (outcome != "rejected" ||
                            !evidence.GetProperty("GuardPreflightPassed").GetBoolean() || evidence.GetProperty("ErrorType").GetString() != "KeyNotFoundException" ||
                            !evidence.GetProperty("ProductionMetadataFallback").GetBoolean())) throw new InvalidDataException("Late Ogg stream did not exercise actual library exception and production fallback.");
                        if (before != Hash(path)) throw new InvalidDataException("Decoder changed owned input bytes.");
                        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                        results.Add(new { Seed = name, Kind = kind, Mutation = mutation, Sha256 = before, Outcome = outcome, child.ExitCode, child.PeakCommittedBytes, child.WallMilliseconds, SourceUnchanged = true, Evidence = evidence.Clone() });
                    }
                    catch (Exception error)
                    {
                        var detail = error.Message[..Math.Min(error.Message.Length, 2048)]; failures.Add(name + "/" + kind + "/" + mutation + ": " + detail);
                        results.Add(new { Seed = name, Kind = kind, Mutation = mutation, Outcome = "worker-failed", Detail = detail, WallMilliseconds = clock.Elapsed.TotalMilliseconds });
                    }
                }
            }
            var hybrid = Path.Combine(root, "correction.wv"); var sidecar = Path.ChangeExtension(hybrid, ".wvc");
            File.Copy(Path.Combine(fixtures, "audio-extended/hybrid-corrected.wv"), hybrid);
            var correction = File.ReadAllBytes(Path.Combine(fixtures, "audio-extended/hybrid-corrected.wvc"));
            foreach (var mutation in new[] { "control", "truncated", "body-bits", "declared-size" })
            {
                File.WriteAllBytes(sidecar, Mutate(correction, ".wvc", mutation));
                var audioHash = Hash(hybrid); var correctionHash = Hash(sidecar);
                File.WriteAllText(requestPath, JsonSerializer.Serialize(new Request("decode", hybrid)));
                try
                {
                    var child = await SecurityChildProcess.RunAsync(tool, root, requestPath);
                    if (child.ExitCode != 0) throw new InvalidOperationException("Correction worker failed: " + child.Output + child.Errors);
                    using var json = JsonDocument.Parse(child.Output); var evidence = json.RootElement;
                    var outcome = evidence.GetProperty("Outcome").GetString();
                    if (evidence.GetProperty("Status").GetString() != "bounded-parser-worker-completed" || outcome is not ("decoded" or "rejected") || mutation == "control" && outcome != "decoded") throw new InvalidDataException("Correction worker evidence invalid.");
                    if (Hash(hybrid) != audioHash || Hash(sidecar) != correctionHash) throw new InvalidDataException("Correction decoder changed owned source data.");
                    using (File.Open(hybrid, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    using (File.Open(sidecar, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    results.Add(new { Seed = "audio-extended/hybrid-corrected.wv", Kind = "correction", Mutation = mutation, Sha256 = audioHash, CorrectionSha256 = correctionHash, Outcome = outcome, child.PeakCommittedBytes, child.WallMilliseconds, SourceUnchanged = true, Evidence = evidence.Clone() });
                }
                catch (Exception error) { failures.Add("correction/" + mutation + ": " + error.Message[..Math.Min(error.Message.Length, 2048)]); }
            }
            foreach (var name in hashes.Keys) if (Hash(Path.Combine(fixtures, name)) != hashes[name]) throw new InvalidDataException("Tracked synthetic seed changed.");
            var report = new { Status = failures.Count == 0 ? "bounded-parser-security-passed" : "bounded-parser-security-failed", Seeds = sources.Length, Cases = results.Count,
                Isolation = "Fresh child per case in disposable Windows CI/lab; Job Object resource containment is not a security sandbox",
                MemoryLimitBytes = SecurityChildProcess.MemoryBytes, UserCpuLimitSeconds = SecurityChildProcess.CpuSeconds, WallLimitSeconds = SecurityChildProcess.WallSeconds,
                StdoutAndStderrLimitCharacters = 65536, AssignedLimitsQueried = true, TimeoutControlKilledAndWaited = true,
                NativeFiles = nativeFiles, SeedSha256 = hashes, OriginalSeedHashesUnchanged = true, Results = results, Failures = failures,
                Coverage = "Deterministic header/body/tail/size mutations, guarded TagLib reads, seek/reopen/EOF and WavPack correction controls; not coverage-guided native fuzzing or proof of memory safety" };
            var output = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke", "security-parsers.json"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (failures.Count != 0) throw new InvalidOperationException($"{failures.Count} bounded parser cases failed. See artifacts/smoke/security-parsers.json.");
            return report;
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task<object> WorkerAsync(string request)
    {
        SetErrorMode(0x0001 | 0x0002 | 0x8000); // Child-only: no modal critical-error/WER/open-file dialogs.
        if (await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "bounded-security-worker") throw new InvalidOperationException("Worker requires parent resource assignment.");
        var root = Path.GetFullPath(Environment.CurrentDirectory); LocalFileAccess.ValidateDirectory(root);
        var marker = Path.Combine(root, Marker);
        if (!Path.GetFileName(root).StartsWith(Prefix, StringComparison.Ordinal) || new FileInfo(marker).Length > 128 ||
            !Guid.TryParseExact(File.ReadAllText(marker).Trim(), "N", out var token) || Path.GetFileName(root) != Prefix + token.ToString("N")) throw new InvalidDataException("Worker requires its owned token workspace.");
        request = LocalFileAccess.ValidateFile(request);
        if (Path.GetDirectoryName(request) != root || new FileInfo(request).Length > 4096) throw new InvalidDataException("Request must be bounded and owned.");
        var input = JsonSerializer.Deserialize<Request>(File.ReadAllText(request)) ?? throw new InvalidDataException("Empty worker request.");
        if (Path.GetDirectoryName(input.Source) != root || new FileInfo(input.Source).Length > 4 * 1024 * 1024) throw new InvalidDataException("Worker input must be small and owned.");
        if (input.Kind == "timeout-control") { await Task.Delay(Timeout.InfiniteTimeSpan); throw new InvalidOperationException("Timeout control unexpectedly completed."); }
        if (input.Kind == "lease") return RestrictedFileSecurityValidation.Run(input.Source);
        if (input.Kind == "metadata") return MetadataWorker(input.Source);
        if (input.Kind == "lifecycle")
        {
            NativeLibraryBootstrap.LoadAndVerify();
            return new { Status = "bounded-parser-worker-completed", Outcome = "lifecycle-passed", Lifecycle = DecoderLifecycleValidation.Run(input.Source) };
        }
        if (input.Kind != "decode") throw new InvalidDataException("Unknown worker request.");
        NativeLibraryBootstrap.LoadAndVerify(); // Dependency/loader failures never count as parser rejection.
        using var session = new BassSmokeSession();
        var clock = Stopwatch.StartNew();
        try { var decoded = session.Decode(input.Source); return new { Status = "bounded-parser-worker-completed", Outcome = "decoded", Decode = decoded, DecodeMilliseconds = clock.Elapsed.TotalMilliseconds }; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        {
            // A fresh real decoder must remain usable after rejecting this input.
            using var recovery = new BassSmokeSession(); var decoded = recovery.Decode(Path.Combine(root, "lease-control.wav"));
            return new { Status = "bounded-parser-worker-completed", Outcome = "rejected", Error = error.Message[..Math.Min(error.Message.Length, 1024)], ValidControlRecovered = decoded.DecodedBytes > 0, DecodeMilliseconds = clock.Elapsed.TotalMilliseconds };
        }
    }
    private static byte[] Mutate(byte[] source, string extension, string mutation)
    {
        if (mutation == "body-truncated") return source[..(source.Length * 3 / 4)];
        if (mutation == "truncated") return source[..Math.Min(128, source.Length / 2)];
        var bytes = source.ToArray();
        if (mutation == "late-stream")
        {
            var position = 0; var last = 0;
            while (position < bytes.Length)
            {
                if (position > bytes.Length - 27 || !bytes.AsSpan(position, 4).SequenceEqual("OggS"u8)) throw new InvalidDataException("Owned late-stream seed is not Ogg.");
                last = position; var count = bytes[position + 26];
                if (position + 27 + count > bytes.Length) throw new InvalidDataException("Owned Ogg laces truncated.");
                var body = 0; foreach (var lace in bytes.AsSpan(position + 27, count)) body += lace;
                position = checked(position + 27 + count + body);
                if (position > bytes.Length) throw new InvalidDataException("Owned Ogg body truncated.");
            }
            var serial = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(last + 14));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(last + 14), serial ^ 0x80000000u);
        }
        if (mutation == "header-bits")
        { var random = new Random(0x4d5053); for (var i = 0; i < 16; i++) bytes[random.Next(Math.Min(96, bytes.Length))] ^= (byte)(1 << random.Next(8)); }
        if (mutation is "body-bits" or "tail-bits")
        {
            var random = new Random(0x4d5053); var start = mutation == "body-bits" ? source.Length / 3 : source.Length * 3 / 4;
            var end = mutation == "body-bits" ? source.Length * 2 / 3 : source.Length;
            for (var i = 0; i < 32; i++) bytes[random.Next(start, end)] ^= (byte)(1 << random.Next(8));
        }
        if (mutation == "declared-size")
        {
            if (extension == ".wav") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), uint.MaxValue);
            else if (extension == ".flac") bytes.AsSpan(5, 3).Fill(255);
            else if (extension == ".mp3") { bytes = "ID3\u0004\0\0\u007f\u007f\u007f\u007f"u8.ToArray().Concat(bytes).ToArray(); }
            else bytes.AsSpan(8, Math.Min(16, bytes.Length - 8)).Fill(255);
        }
        return bytes;
    }
    private static object MetadataWorker(string source)
    {
        using var lease = LocalReadLease.Open(source);
        var guarded = false;
        try
        {
            Player.Core.Media.MetadataReadGuard.Validate(lease.Path); guarded = true;
            using var file = TagLib.File.Create(lease.Path, TagLib.ReadStyle.Average);
            var title = file.Tag.Title; var pictures = file.Tag.Pictures;
            return new { Status = "bounded-parser-worker-completed", Outcome = "metadata-parsed", TitleCharacters = title?.Length ?? 0, Pictures = pictures.Length,
                PictureBytes = pictures.Sum(p => (long)p.Data.Count), GuardedRead = true };
        }
        catch (Exception error) when (error is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or InvalidDataException or ArgumentException or NotImplementedException or KeyNotFoundException)
        {
            var diagnostics = new List<string>();
            var fallback = Player.App.Services.Library.MediaMetadataReader.Read(source, Guid.NewGuid(), diagnostics.Add);
            if (diagnostics.Count == 0 || fallback.Path != source || !fallback.Available) throw new InvalidDataException("Production metadata fallback was not preserved.");
            // Same process must still parse a valid source after this rejection.
            var control = Path.Combine(Environment.CurrentDirectory, "lease-control.wav");
            Player.Core.Media.MetadataReadGuard.Validate(control);
            using var file = TagLib.File.Create(control, TagLib.ReadStyle.Average);
            if (file.Properties.AudioSampleRate <= 0) throw new InvalidDataException("Metadata rejection damaged the valid control.");
            return new { Status = "bounded-parser-worker-completed", Outcome = "rejected", ErrorType = error.GetType().Name, GuardPreflightPassed = guarded, Error = error.Message[..Math.Min(error.Message.Length, 1024)], ValidMetadataControlRecovered = true, ProductionMetadataFallback = true };
        }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) if (!file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
