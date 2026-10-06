using System.Text.Json;
using System.Text.Json.Serialization;
using Player.Core.Media;

namespace Player.Core.Integration;

/// <summary>The entire IPC command surface: append local paths, optionally play, or activate.</summary>
public sealed record OpenRequest(int Version, string[] Paths, bool Play = false)
{
    public const int MaximumBytes = 65536;
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4 };
    public static OpenRequest ParseArguments(IEnumerable<string> arguments, string directory)
    {
        var paths = new List<string>(); var play = false; var literal = false;
        foreach (var argument in arguments)
        {
            if (!literal && argument == "--") { literal = true; continue; }
            if (!literal && argument == "--play") { play = true; continue; }
            if (!literal && argument.StartsWith('-')) throw new ArgumentException("Unknown option. Use local paths, --play, or -- before paths.");
            if (string.IsNullOrWhiteSpace(argument) || argument.Length > 32767) throw new ArgumentException("Invalid local path.");
            // Validate before resolving, so a URL can never become an apparently local relative path.
            if (argument.Contains("://", StringComparison.Ordinal) || argument.StartsWith("\\\\", StringComparison.Ordinal) || argument.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("Only local paths are accepted.");
            paths.Add(LocalMediaPath.Parse(argument.Length >= 3 && argument[1] == ':' ? argument : directory.TrimEnd('\\', '/') + "\\" + argument).Value);
            if (paths.Count > 1000) throw new ArgumentException("Too many paths.");
        }
        var request = new OpenRequest(1, paths.ToArray(), play); request.Encode(); return request;
    }
    public void Validate()
    {
        if (Version != 1 || Paths is null || Paths.Length > 1000) throw new InvalidDataException("Invalid open request.");
        foreach (var path in Paths) LocalMediaPath.Parse(path);
    }
    public byte[] Encode()
    {
        Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Open request is too large.");
        return bytes;
    }
    public static OpenRequest Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumBytes) throw new InvalidDataException("Open request size is invalid.");
        var request = JsonSerializer.Deserialize<OpenRequest>(bytes, Options) ?? throw new InvalidDataException("Empty open request.");
        request.Validate(); return request;
    }
}
