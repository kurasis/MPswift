using System.Text.Json;
using Player.Core.Integration;

namespace Player.Core.Tests;

public sealed class DiagnosticPrivacyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReportRedactsConfiguredUnicodeDirectoryInPlainAndJsonPaths(bool forwardSlashes, bool json)
    {
        const string prefix = @"C:\Users\Личный 🎵";
        var path = prefix + @"\Music\song.flac";
        if (forwardSlashes) path = path.Replace('\\', '/');
        var input = json ? JsonSerializer.Serialize(new { Path = path }) : path;

        var report = DiagnosticReport.Redact(input, [prefix + "\\"]);
        using var parsed = json ? JsonDocument.Parse(report) : null;
        var actual = parsed is not null ? parsed.RootElement.GetProperty("Path").GetString() : report;

        Assert.Equal("<local>" + (forwardSlashes ? "/Music/song.flac" : @"\Music\song.flac"), actual);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LogRedactsPathsBeforeSerializingBothFieldsAndRetainsValidJson(bool forwardSlashes, bool operationPath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mp-privacy-" + Guid.NewGuid().ToString("N"));
        const string prefix = @"C:\Users\Личный 🎵";
        var path = prefix + @"\Music\song.flac";
        if (forwardSlashes) path = path.Replace('\\', '/');
        var nestedJson = JsonSerializer.Serialize(new { Path = path });
        try
        {
            await using (var log = new RotatingLog(directory, [prefix + "\\"]))
                Assert.True(log.Record(operationPath ? prefix.Replace('\\', '/') + "/operation" : "decode", nestedJson));
            using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "log-0.jsonl")));
            Assert.Equal(operationPath ? "<local>/operation" : "decode", record.RootElement.GetProperty("Operation").GetString());
            using var detail = JsonDocument.Parse(record.RootElement.GetProperty("Detail").GetString()!);
            Assert.Equal("<local>" + (forwardSlashes ? "/Music/song.flac" : @"\Music\song.flac"), detail.RootElement.GetProperty("Path").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void SimilarDirectoryNamesAndUnconfiguredPathsRemainVisible()
    {
        const string input = @"C:\Users\PrivateOther C:\Users\Private-Other C:\Users\Private_2 D:\Public\song.flac";
        Assert.Equal(input, DiagnosticReport.Redact(input, [@"C:\Users\Private"]));
    }
}
