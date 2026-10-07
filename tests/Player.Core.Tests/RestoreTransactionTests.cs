using System.Text;
using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class RestoreTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-restore-transaction-" + Guid.NewGuid().ToString("N"));
    public RestoreTransactionTests() => Directory.CreateDirectory(_root);
    private string Db => Path.Combine(_root, "library.db");
    private string Settings => Path.Combine(_root, "settings.json");
    [Fact]
    public void RepeatedUnicodePublicationsUseExactlyTheRequestedNames()
    {
        for (var i = 0; i < 64; i++)
        {
            var destination = Path.Combine(_root, "Архив-" + new string('x', i) + ".zip");
            RestoreFileTransaction.PublishNew(destination, stream => stream.WriteByte((byte)i));
            Assert.Equal(new[] { (byte)i }, File.ReadAllBytes(destination));
        }
        Assert.Equal(64, Directory.GetFiles(_root).Length);
        Assert.Empty(Directory.GetFiles(_root, "*.partial-*"));
    }
    [Fact]
    public void FailedArchivePublicationRemovesItsOwnedPartialAndPreservesExistingDestination()
    {
        var destination = Path.Combine(_root, "backup.zip");
        var failure = new IOException("Owned publication failure.");
        Assert.Same(failure, Assert.Throws<IOException>(() => RestoreFileTransaction.PublishNew(destination, stream => { stream.WriteByte(1); throw failure; })));
        Assert.Empty(Directory.GetFiles(_root));
        File.WriteAllText(destination, "foreign destination");
        Assert.Throws<IOException>(() => RestoreFileTransaction.PublishNew(destination, stream => stream.WriteByte(2)));
        Assert.Equal("foreign destination", File.ReadAllText(destination)); Assert.Empty(Directory.GetFiles(_root, "*.partial-*"));
    }

    [Fact]
    public void SuccessfulInstallRetainsOriginalBytesAndReleasesInstalledFiles()
    {
        File.WriteAllText(Db, "original database"); File.WriteAllText(Settings, "original settings");
        using var db = new MemoryStream(Encoding.UTF8.GetBytes("new database"));
        using var settings = new MemoryStream(Encoding.UTF8.GetBytes("new settings"));
        RestoreFileTransaction.Install([(Db, db), (Settings, settings)], [(Db, Db + ".preserved", false), (Settings, Settings + ".preserved", false)]);
        Assert.Equal("new database", File.ReadAllText(Db)); Assert.Equal("new settings", File.ReadAllText(Settings));
        Assert.Equal("original database", File.ReadAllText(Db + ".preserved")); Assert.Equal("original settings", File.ReadAllText(Settings + ".preserved"));
        using var reopened = File.Open(Db, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void FailureAfterFirstInstallRollsBackBothOriginalsAndRemovesOnlyOwnedCopies()
    {
        File.WriteAllText(Db, "original database"); File.WriteAllText(Settings, "original settings");
        using var db = new MemoryStream([1, 2, 3]); using var settings = new MemoryStream([4, 5, 6]);
        var failure = new IOException("Owned test interruption after first install.");
        var error = Assert.Throws<IOException>(() => RestoreFileTransaction.Install([(Db, db), (Settings, settings)],
            [(Db, Db + ".preserved", false), (Settings, Settings + ".preserved", false)], count => { if (count == 1) throw failure; }));
        Assert.Same(failure, error);
        Assert.Equal("original database", File.ReadAllText(Db)); Assert.Equal("original settings", File.ReadAllText(Settings));
        Assert.Empty(Directory.GetFiles(_root, "*.preserved")); Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void OccupiedPreservationNameNeverOverwritesItsContents()
    {
        File.WriteAllText(Db, "original database"); File.WriteAllText(Db + ".preserved", "foreign sentinel");
        using var source = new MemoryStream([1, 2]);
        Assert.Throws<IOException>(() => RestoreFileTransaction.Install([(Db, source)], [(Db, Db + ".preserved", false)]));
        Assert.Equal("original database", File.ReadAllText(Db)); Assert.Equal("foreign sentinel", File.ReadAllText(Db + ".preserved"));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void IncompleteRollbackRetainsOriginalsAndReportsBothFailures()
    {
        File.WriteAllText(Db, "original database"); File.WriteAllText(Settings, "original settings");
        using var db = new MemoryStream([1]); using var settings = new MemoryStream([2]);
        var error = Assert.Throws<AggregateException>(() => RestoreFileTransaction.Install([(Db, db), (Settings, settings)],
            [(Db, Db + ".preserved", false), (Settings, Settings + ".preserved", false)], count =>
            { if (count == 1) { File.WriteAllText(Settings, "foreign collision"); throw new IOException("Owned interruption."); } }));
        Assert.True(error.InnerExceptions.Count >= 2); Assert.Equal("original database", File.ReadAllText(Db));
        Assert.Equal("original settings", File.ReadAllText(Settings + ".preserved")); Assert.Equal("foreign collision", File.ReadAllText(Settings));
    }

    [Fact]
    public async Task ConcurrentSettingsReadersObserveCommittedSettingsAcrossDistinctInstances()
    {
        new SettingsFile(_root).Save(new(Volume: 20));
        await Task.WhenAll(Task.Run(() => { for (var i = 0; i < 20; i++) new SettingsFile(_root).Save(new(Volume: i % 2 == 0 ? 20 : 80)); }),
            Task.Run(() => { for (var i = 0; i < 100; i++) Assert.Contains(new SettingsFile(_root).Load().Volume, new[] { 20d, 80d }); }));
        Assert.NotNull(new SettingsFile(_root).LoadBackup());
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*")); Assert.Empty(Directory.GetFiles(_root, "*.previous-*"));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
