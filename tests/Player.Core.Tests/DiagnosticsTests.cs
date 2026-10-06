using System.Text.Json;
using Player.Core.Integration;

namespace Player.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task LocalLogsRotateAtBoundAndRedactBeforeWritingCompleteJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mp-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new RotatingLog(directory, ["C:\\Users\\Private"], maximumBytes: 4096, files: 3);
            for (var i = 0; i < 100; i++)
            { while (!log.Record("decode", "C:\\Users\\Private\\Музыка 🎵.wav\n" + new string('x', 1000))) await Task.Delay(1); }
            await log.DisposeAsync(); Assert.Null(log.LastError);
            var files = Directory.GetFiles(directory); Assert.Equal(3, files.Length);
            foreach (var file in files)
            {
                Assert.InRange(new FileInfo(file).Length, 1, 4096);
                var text = File.ReadAllText(file); Assert.DoesNotContain("Private", text);
                foreach (var line in File.ReadLines(file)) { using var json = JsonDocument.Parse(line); Assert.Contains("<local>", json.RootElement.GetProperty("Detail").GetString()); }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task LogFailureIsReportedWithoutThrowingOnTheCaller()
    {
        var file = Path.GetTempFileName();
        try { var log = new RotatingLog(file); Assert.True(log.Record("startup", "test")); await log.DisposeAsync(); Assert.NotNull(log.LastError); }
        finally { File.Delete(file); }
    }
}
