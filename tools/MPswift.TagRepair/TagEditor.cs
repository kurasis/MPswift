using System.Reflection;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Player.Core.Media;

namespace MPswift.TagRepair;

internal sealed record TagEdit(Dictionary<string, string> Before, Dictionary<string, string> After, string[] Changes, Dictionary<string, string> Preserved);

internal static class TagEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    // TagLib's public standard text fields include sort names, composer, lyrics and comments.
    // Technical identifiers are ASCII; preserve them even when a malformed tag claims otherwise.
    private static readonly PropertyInfo[] Fields = typeof(TagLib.Tag).GetProperties().Where(property => property.CanRead && property.CanWrite &&
        (property.PropertyType == typeof(string) || property.PropertyType == typeof(string[])) &&
        !property.Name.StartsWith("Music", StringComparison.Ordinal) && !property.Name.Contains("Id", StringComparison.Ordinal) &&
        property.Name is not "ISRC" and not "InitialKey").ToArray();

    internal static TagEdit Read(Stream stream, string name)
    {
        MetadataReadGuard.Validate(stream, name);
        using var file = TagLib.File.Create(new BorrowedStream(stream, name, false), TagLib.ReadStyle.Average);
        var before = Snapshot(file.Tag);
        var after = Recovered(file.Tag, name);
        return new(before, after, before.Keys.Where(key => before[key] != after[key]).ToArray(), Preservation(file.Tag));
    }

    internal static void Rewrite(Stream stream, string name, TagEdit expected)
    {
        var version = TagLib.Id3v2.Tag.DefaultVersion; var encoding = TagLib.Id3v2.Tag.DefaultEncoding;
        var forceVersion = TagLib.Id3v2.Tag.ForceDefaultVersion; var forceEncoding = TagLib.Id3v2.Tag.ForceDefaultEncoding;
        try
        {
            TagLib.Id3v2.Tag.DefaultVersion = 4; TagLib.Id3v2.Tag.DefaultEncoding = TagLib.StringType.UTF8;
            TagLib.Id3v2.Tag.ForceDefaultVersion = true; TagLib.Id3v2.Tag.ForceDefaultEncoding = true;
            using (var file = TagLib.File.Create(new BorrowedStream(stream, name, true), TagLib.ReadStyle.Average))
            {
                var tag = file.Tag;
                if (Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    var unicode = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
                    tag.CopyTo(unicode, true); unicode.Version = 4;
                    file.RemoveTags(TagLib.TagTypes.Id3v1);
                    tag = file.Tag;
                }
                foreach (var field in Fields)
                {
                    var json = expected.After[field.Name];
                    field.SetValue(tag, JsonSerializer.Deserialize(json, field.PropertyType));
                }
                file.Save();
            }
            MetadataReadGuard.Validate(stream, name);
            using var verify = TagLib.File.Create(new BorrowedStream(stream, name, false), TagLib.ReadStyle.Average);
            var actual = Snapshot(verify.Tag);
            foreach (var field in expected.After)
                if (actual[field.Key] != field.Value) throw new InvalidDataException("Written tag differs from the preview: " + field.Key);
            var preserved = Preservation(verify.Tag);
            foreach (var field in expected.Preserved)
                if (preserved[field.Key] != field.Value) throw new InvalidDataException("Non-text metadata changed; original retained: " + field.Key);
            if (Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                (((TagLib.Id3v2.Tag)verify.GetTag(TagLib.TagTypes.Id3v2, false)).Version != 4 || (verify.TagTypesOnDisk & TagLib.TagTypes.Id3v1) != 0))
                throw new InvalidDataException("MP3 did not retain ID3v2.4 without a lossy ID3v1 copy.");
        }
        finally
        {
            TagLib.Id3v2.Tag.DefaultVersion = version; TagLib.Id3v2.Tag.DefaultEncoding = encoding;
            TagLib.Id3v2.Tag.ForceDefaultVersion = forceVersion; TagLib.Id3v2.Tag.ForceDefaultEncoding = forceEncoding;
        }
    }

    internal static bool NeedsUnicodeRewrite(Stream stream, string name)
    {
        if (!Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return false;
        using var file = TagLib.File.Create(new BorrowedStream(stream, name, false), TagLib.ReadStyle.Average);
        if ((file.TagTypesOnDisk & TagLib.TagTypes.Id3v1) != 0 || file.GetTag(TagLib.TagTypes.Id3v2, false) is not TagLib.Id3v2.Tag tag || tag.Version != 4) return true;
        return tag.GetFrames().Any(frame => frame is TagLib.Id3v2.TextInformationFrame text && text.TextEncoding != TagLib.StringType.UTF8 ||
            frame is TagLib.Id3v2.CommentsFrame comment && comment.TextEncoding != TagLib.StringType.UTF8 ||
            frame is TagLib.Id3v2.UnsynchronisedLyricsFrame lyrics && lyrics.TextEncoding != TagLib.StringType.UTF8);
    }

    private static object? Value(PropertyInfo field, TagLib.Tag tag)
    {
        var value = Bounded(field.GetValue(tag));
        // ID3v1 has no roles; ID3v2 pads unassigned roles to performer count.
        // Trim only the unassigned tail, keeping every assigned role at its original index.
        if (field.Name == "PerformersRole" && value is string[] roles)
        {
            var length = roles.Length;
            while (length > 0 && string.IsNullOrEmpty(roles[length - 1])) length--;
            return roles[..length];
        }
        return value;
    }
    private static Dictionary<string, string> Snapshot(TagLib.Tag tag) => Fields.ToDictionary(field => field.Name, field => JsonSerializer.Serialize(Value(field, tag), JsonOptions));
    private static Dictionary<string, string> Recovered(TagLib.Tag tag, string context) => Fields.ToDictionary(field => field.Name, field =>
        JsonSerializer.Serialize(Value(field, tag) switch
        {
            string text => (object?)TextRepair.Recover(text, context),
            string[] texts => texts.Select(text => TextRepair.Recover(text, context)).ToArray(),
            _ => null
        }, JsonOptions));

    private static object? Bounded(object? value)
    {
        if (value is string text && text.Length > 65536 || value is string[] values && (values.Length > 1024 || values.Sum(text => (long)(text?.Length ?? 0)) > 65536))
            throw new InvalidDataException("A tag text field exceeds 65536 characters; original retained.");
        return value;
    }

    private static Dictionary<string, string> Preservation(TagLib.Tag tag)
    {
        var values = typeof(TagLib.Tag).GetProperties().Where(property => property.CanRead && property.CanWrite && !Fields.Contains(property) &&
            (property.PropertyType == typeof(string) || property.PropertyType == typeof(string[]) || property.PropertyType.IsPrimitive))
            .ToDictionary(property => property.Name, property => JsonSerializer.Serialize(Bounded(property.GetValue(tag)), JsonOptions));
        values["Pictures"] = JsonSerializer.Serialize(tag.Pictures.Select(picture => new { picture.Type, picture.MimeType, picture.Description,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(picture.Data.Data)) }), JsonOptions);
        return values;
    }

    private sealed class BorrowedStream(Stream stream, string name, bool writable) : TagLib.File.IFileAbstraction
    {
        public string Name => name;
        public Stream ReadStream => stream;
        public Stream WriteStream => writable ? stream : throw new NotSupportedException("Preview is read-only.");
        public void CloseStream(Stream borrowed) { if (writable) borrowed.Flush(); }
    }
}
