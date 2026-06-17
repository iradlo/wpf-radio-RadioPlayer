using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioPlayer.Services.Audio;

/// <summary>
/// Wraps a SHOUTcast/Icecast network stream: strips interleaved ICY metadata blocks
/// (raising <see cref="StreamTitleChanged"/> for song titles) and guarantees that
/// <see cref="Read"/> returns exactly the requested number of bytes, which the NAudio
/// MP3 frame parser requires. Throws <see cref="EndOfStreamException"/> when the
/// server closes the connection.
/// </summary>
internal sealed partial class IcyReadFullyStream : Stream
{
    private const int MetadataBlockMultiplier = 16;

    private readonly Stream _source;
    private readonly int _metadataInterval;
    private int _bytesUntilMetadata;
    private long _position;
    private string? _lastTitle;

    /// <param name="source">The raw network stream.</param>
    /// <param name="metadataInterval">
    /// Value of the icy-metaint response header; 0 when the server sends no metadata.
    /// </param>
    public IcyReadFullyStream(Stream source, int metadataInterval)
    {
        _source = source;
        _metadataInterval = metadataInterval;
        _bytesUntilMetadata = metadataInterval;
    }

    /// <summary>Raised when the StreamTitle in the ICY metadata changes.</summary>
    public event Action<string>? StreamTitleChanged;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    /// <summary>
    /// Total audio bytes returned so far. NAudio's <c>Mp3Frame.LoadFromStream</c>
    /// reads this for bookkeeping, so the getter must not throw. Seeking (the setter)
    /// stays unsupported.
    /// </summary>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var toRead = count - totalRead;
            if (_metadataInterval > 0)
            {
                if (_bytesUntilMetadata == 0)
                {
                    ReadMetadataBlock();
                    _bytesUntilMetadata = _metadataInterval;
                }

                toRead = Math.Min(toRead, _bytesUntilMetadata);
            }

            var read = _source.Read(buffer, offset + totalRead, toRead);
            if (read == 0)
            {
                throw new EndOfStreamException("The radio stream was closed by the server.");
            }

            totalRead += read;
            _position += read;
            if (_metadataInterval > 0)
            {
                _bytesUntilMetadata -= read;
            }
        }

        return totalRead;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }

    [GeneratedRegex("StreamTitle='([^']*)'", RegexOptions.IgnoreCase)]
    private static partial Regex StreamTitleRegex();

    private void ReadMetadataBlock()
    {
        var lengthByte = _source.ReadByte();
        if (lengthByte < 0)
        {
            throw new EndOfStreamException("The radio stream was closed by the server.");
        }

        var length = lengthByte * MetadataBlockMultiplier;
        if (length == 0)
        {
            return;
        }

        var metadata = new byte[length];
        var read = 0;
        while (read < length)
        {
            var chunk = _source.Read(metadata, read, length - read);
            if (chunk == 0)
            {
                throw new EndOfStreamException("The radio stream was closed by the server.");
            }

            read += chunk;
        }

        var text = Encoding.UTF8.GetString(metadata).TrimEnd('\0');
        var match = StreamTitleRegex().Match(text);
        if (match.Success)
        {
            var title = match.Groups[1].Value.Trim();
            if (title.Length > 0 && title != _lastTitle)
            {
                _lastTitle = title;
                StreamTitleChanged?.Invoke(title);
            }
        }
    }
}
