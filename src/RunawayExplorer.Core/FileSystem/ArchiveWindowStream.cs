namespace RunawayExplorer.Core.FileSystem;

/// <summary>
/// A read-only view of one entry inside an archive file: a fixed <c>[offset, offset + length)</c> window
/// over a <see cref="FileStream"/>, so a 100 MB archive never has to be read whole to serve one entry.
/// Owns the underlying stream and closes it on dispose.
/// </summary>
public sealed class ArchiveWindowStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public ArchiveWindowStream(Stream inner, long start, long length)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (start < 0 || length < 0 || start + length > inner.Length)
            throw new ArgumentOutOfRangeException(nameof(length), "Window lies outside the underlying stream.");

        _inner = inner;
        _start = start;
        _length = length;
        _inner.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
            _inner.Position = _start + value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long remaining = _length - _position;
        if (remaining <= 0)
            return 0;
        if (buffer.Length > remaining)
            buffer = buffer[..(int)remaining];
        _inner.Position = _start + _position;
        int read = _inner.Read(buffer);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        Position = target;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
