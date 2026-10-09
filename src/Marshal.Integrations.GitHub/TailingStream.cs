using System.Text;
using System.Text.RegularExpressions;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// A read-only pass-through that remembers the last bytes read, so a log can be stored and its tail returned from
/// one read of the download. <see cref="DrainAsync"/> reads what a consumer left unread (e.g. after a size limit).
/// </summary>
internal sealed partial class TailingStream(Stream inner) : Stream
{
    private const int Window = 64 * 1024;

    private readonly byte[] _ring = new byte[Window];
    private long _total;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => _total; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Remember(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken);
        return Remember(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Reads the rest of the stream so the tail is complete.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        while (await ReadAsync(buffer, cancellationToken) > 0)
        {
        }
    }

    /// <summary>The last <paramref name="lines"/> lines read, without GitHub's timestamp prefixes.</summary>
    public IReadOnlyList<string> Tail(int lines)
    {
        int length = (int)Math.Min(_total, Window);
        byte[] last = new byte[length];
        for (int index = 0; index < length; index++)
            last[index] = _ring[(int)((_total - length + index) % Window)];

        string[] all = Encoding.UTF8.GetString(last).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        // The first line may be cut by the window.
        IEnumerable<string> whole = _total > Window ? all.Skip(1) : all;
        return [.. whole.TakeLast(lines).Select(line => Timestamp().Replace(line, ""))];
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
            inner.Dispose();
        base.Dispose(disposing);
    }

    private int Remember(ReadOnlySpan<byte> read)
    {
        foreach (byte value in read)
            _ring[(int)(_total++ % Window)] = value;
        return read.Length;
    }

    // "2026-10-09T12:00:00.1234567Z " at the start of every Actions log line.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z ")]
    private static partial Regex Timestamp();
}
