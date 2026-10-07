using System.IO;
using System.Text;
using System.Text.Json;
using Player.Core.Library;

namespace Player.App.Services.Storage;

/// <summary>Enforce the existing UTF-16 session limit while streaming, before a large JSON string is created.</summary>
internal static class SessionJson
{
    public const int MaximumCharacters = 16 * 1024 * 1024;
    public static string Serialize(SessionState session, int maximumCharacters = MaximumCharacters)
    {
        using var output = new LimitedOutput(maximumCharacters);
        JsonSerializer.Serialize(output, session);
        return output.GetText();
    }

    private sealed class LimitedOutput(int maximumCharacters) : Stream
    {
        private readonly MemoryStream _bytes = new();
        private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
        private long _characters;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var remaining = buffer;
            Span<char> characters = stackalloc char[4096];
            while (!remaining.IsEmpty)
            {
                _decoder.Convert(remaining, characters, flush: false, out var consumed, out var written, out _);
                _characters += written;
                if (_characters > maximumCharacters)
                    throw new InvalidDataException("Saved queue/history exceeds the session size limit. Remove some queued items before saving.");
                remaining = remaining[consumed..];
            }
            _bytes.Write(buffer);
        }
        public string GetText()
        {
            _characters += _decoder.GetCharCount(ReadOnlySpan<byte>.Empty, flush: true);
            if (_characters > maximumCharacters) throw new InvalidDataException("Saved session exceeds its character limit.");
            return Encoding.UTF8.GetString(_bytes.GetBuffer().AsSpan(0, checked((int)_bytes.Length)));
        }
        protected override void Dispose(bool disposing) { if (disposing) _bytes.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes.Length;
        public override long Position { get => _bytes.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
