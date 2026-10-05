using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Player.App.Services.Audio;
using Player.AudioSmoke;

var json = new JsonSerializerOptions { WriteIndented = true };
void Report(object result) => Console.WriteLine(JsonSerializer.Serialize(result, json));

if (args.Length == 0 || args[0] is not ("--generate-fixture" or "--probe" or "--decode" or "--play") ||
    args.Length != (args[0] == "--probe" ? 1 : 2))
{
    Console.Error.WriteLine("Usage: Player.AudioSmoke --generate-fixture <new.wav> | --probe | --decode <local-file> | --play <local-file>");
    return 2;
}

try
{
    if (args[0] == "--generate-fixture")
    {
        Report(new { Status = "generated", Fixture = WavFixture.Generate(args[1]) });
        return 0;
    }
    if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    {
        Report(new { Status = "not-run", Reason = "BASS/WASAPI smoke requires Windows x64.", Environment = RuntimeInformation.OSDescription });
        return 3;
    }
    var versions = NativeLibraryBootstrap.LoadAndVerify();
    if (args[0] == "--probe")
    {
        Report(new { Status = "native-load-passed", Versions = versions, Environment = RuntimeInformation.OSDescription });
        return 0;
    }
    var path = BassSmokeSession.ValidateSourcePath(Path.GetFullPath(args[1]));
    var before = HashFile(path);
    DecodeEvidence decoded;
    OutputEvidence? output = null;
    using (var session = new BassSmokeSession())
    {
        decoded = session.Decode(path);
        if (args[0] == "--play") output = session.ExerciseSharedOutput();
    }
    // The source must be preserved and closed after native disposal.
    using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
    if (before != HashFile(path)) throw new InvalidDataException("Source checksum changed.");
    if (File.Exists(path + ".json"))
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(path + ".json"));
        var expected = manifest.RootElement;
        if (expected.GetProperty("Sha256").GetString() != before ||
            expected.GetProperty("SampleRate").GetInt32() != decoded.SampleRate ||
            expected.GetProperty("Channels").GetInt32() != decoded.Channels ||
            Math.Abs(expected.GetProperty("DurationSeconds").GetDouble() - decoded.DurationSeconds) > expected.GetProperty("DurationToleranceSeconds").GetDouble() ||
            Math.Abs(expected.GetProperty("ExpectedPeak").GetDouble() - decoded.Peak) > expected.GetProperty("PeakTolerance").GetDouble())
            throw new InvalidDataException("Decoded fixture does not match its manifest.");
    }
    Report(new
    {
        Status = output is null ? "decode-passed" : "output-api-passed",
        Environment = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Versions = versions, SourceSha256 = before, Decode = decoded, Output = output,
        SourceUnchanged = true, SourceHandleReleased = true,
        AudiblePlayback = "not-manually-verified", Gapless = "not-run"
    });
    return 0;
}
catch (Exception error)
{
    Report(new { Status = "failed", ErrorType = error.GetType().Name, error.Message, Environment = RuntimeInformation.OSDescription });
    return 1;
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
