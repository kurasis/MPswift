using System.Text.RegularExpressions;

namespace Player.Core.Media;

/// <summary>Lexical Windows path validation. Filesystem/device checks are a separate Windows responsibility.</summary>
public sealed partial record LocalMediaPath
{
    public string Value { get; }

    private LocalMediaPath(string value) => Value = value;

    public static LocalMediaPath Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > 32767 || !DriveRoot().IsMatch(input))
            throw new ArgumentException("An absolute local Windows drive path is required.", nameof(input));

        var components = new List<string>();
        foreach (var part in input[3..].Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (components.Count == 0) throw new ArgumentException("Path escapes its drive root.", nameof(input));
                components.RemoveAt(components.Count - 1);
                continue;
            }
            if (part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || part.EndsWith('.') || part.EndsWith(' '))
                throw new ArgumentException("Invalid local path component.", nameof(input));
            var stem = part.Split('.')[0];
            if (ReservedDevice().IsMatch(stem))
                throw new ArgumentException("Windows device paths are unsupported.", nameof(input));
            components.Add(part);
        }
        if (components.Count == 0) throw new ArgumentException("A file path is required.", nameof(input));
        return new LocalMediaPath(char.ToUpperInvariant(input[0]) + ":\\" + string.Join('\\', components));
    }

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDevice();
}
