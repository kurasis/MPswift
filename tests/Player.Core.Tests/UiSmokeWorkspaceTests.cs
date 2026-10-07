using Player.App.Services.Storage;

namespace Player.Core.Tests;

public sealed class UiSmokeWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), UiSmokeWorkspace.Prefix + Guid.NewGuid().ToString("N"));
    public UiSmokeWorkspaceTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, UiSmokeWorkspace.Marker), Path.GetFileName(_root)[UiSmokeWorkspace.Prefix.Length..]);
    }

    [Fact]
    public void FreshWorkspaceAndBoundedLanguageSeedAreAcceptedWithoutChanges()
    {
        UiSmokeWorkspace.Validate(_root);
        var data = Path.Combine(_root, "artifacts", "smoke", "stage-c-data");
        Directory.CreateDirectory(data);
        var settings = Path.Combine(data, "settings.json");
        const string contents = "{\"SchemaVersion\":1,\"Language\":\"ru\"}";
        File.WriteAllText(settings, contents);
        UiSmokeWorkspace.Validate(_root);
        Assert.Equal(contents, File.ReadAllText(settings));
    }

    [Theory]
    [InlineData("missing-marker")]
    [InlineData("wrong-token")]
    [InlineData("large-marker")]
    [InlineData("foreign-file")]
    [InlineData("foreign-directory")]
    [InlineData("large-settings")]
    public void UnownedOrNonfreshWorkspaceIsRejectedWithoutWrites(string kind)
    {
        var marker = Path.Combine(_root, UiSmokeWorkspace.Marker);
        switch (kind)
        {
            case "missing-marker": File.Delete(marker); break;
            case "wrong-token": File.WriteAllText(marker, Guid.NewGuid().ToString("N")); break;
            case "large-marker": File.WriteAllText(marker, new string(' ', 129)); break;
            case "foreign-file": File.WriteAllText(Path.Combine(_root, "personal.txt"), "preserve me"); break;
            case "foreign-directory": Directory.CreateDirectory(Path.Combine(_root, "artifacts", "smoke", "stage-e-library")); break;
            case "large-settings":
                var data = Path.Combine(_root, "artifacts", "smoke", "stage-c-data");
                Directory.CreateDirectory(data);
                File.WriteAllBytes(Path.Combine(data, "settings.json"), new byte[65537]); break;
        }
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        Assert.Throws<InvalidDataException>(() => UiSmokeWorkspace.Validate(_root));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void MatchingMarkerDoesNotAuthorizeAnOrdinaryDirectory()
    {
        var ordinary = Path.Combine(_root, "ordinary");
        Directory.CreateDirectory(ordinary);
        File.Copy(Path.Combine(_root, UiSmokeWorkspace.Marker), Path.Combine(ordinary, UiSmokeWorkspace.Marker));
        Assert.Throws<InvalidDataException>(() => UiSmokeWorkspace.Validate(ordinary));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedMarkerOrAllowedOutputDirectoryIsRejected(bool directoryLink)
    {
        var outside = Path.Combine(Path.GetTempPath(), "mpswift-link-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "preserve.txt");
        File.WriteAllText(sentinel, "untouched");
        var link = Path.Combine(_root, directoryLink ? "artifacts" : UiSmokeWorkspace.Marker);
        try
        {
            if (directoryLink) Directory.CreateSymbolicLink(link, outside);
            else
            {
                var marker = Path.Combine(outside, "marker.txt");
                File.Move(link, marker);
                File.CreateSymbolicLink(link, marker);
            }
            Assert.Throws<InvalidDataException>(() => UiSmokeWorkspace.Validate(_root));
            Assert.Equal("untouched", File.ReadAllText(sentinel));
        }
        finally
        {
            if (directoryLink && Directory.Exists(link)) Directory.Delete(link);
            if (!directoryLink && File.Exists(link)) File.Delete(link);
            Directory.Delete(outside, true);
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
