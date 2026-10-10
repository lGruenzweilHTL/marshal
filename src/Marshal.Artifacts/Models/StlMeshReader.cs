using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Marshal.Abstractions;

namespace Marshal.Artifacts.Models;

/// <summary>
/// STL, binary (80-byte header, triangle count, 50 bytes per triangle) or ASCII (<c>solid … vertex x y z …</c>). A
/// binary file may start with <c>solid</c> too, so the size rule decides first. STL has no unit: millimetres assumed.
/// </summary>
internal sealed class StlMeshReader(MeshLimits? limits = null) : IMeshReader
{
    private readonly MeshLimits _limits = limits ?? MeshLimits.Default;

    public Result<Mesh> Read(Stream content, CancellationToken cancellationToken)
    {
        Span<byte> header = stackalloc byte[84];
        int headerLength = content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (headerLength == 84 && content.CanSeek)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
            if (84 + 50L * count == content.Length)
                return count > _limits.MaxTriangles ? MeshErrors.TooManyTriangles(_limits.MaxTriangles) : ReadBinary(content, (int)count, cancellationToken);
        }

        if (headerLength >= 5 && Encoding.ASCII.GetString(header[..5]) == "solid")
            return ReadAscii(header[..headerLength].ToArray(), content, cancellationToken);
        return MeshErrors.Unreadable("not a binary or ASCII STL file");
    }

    private static Result<Mesh> ReadBinary(Stream content, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
            return MeshErrors.Unreadable("it has no triangles");

        float[] triangles = new float[count * 9];
        byte[] record = new byte[50];
        for (int i = 0; i < count; i++)
        {
            if ((i & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (content.ReadAtLeast(record, 50, throwOnEndOfStream: false) < 50)
                return MeshErrors.Unreadable("the file ends early");

            for (int v = 0; v < 9; v++)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(12 + 4 * v));
                if (!float.IsFinite(value))
                    return MeshErrors.Unreadable("it has invalid coordinates");
                triangles[i * 9 + v] = value;
            }
        }

        return new Mesh(triangles, colors: null);
    }

    private Result<Mesh> ReadAscii(byte[] start, Stream rest, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(new ConcatStream(start, rest), Encoding.ASCII);
        List<float> triangles = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ReadOnlySpan<char> text = line.AsSpan().Trim();
            if (!text.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                continue;
            if (triangles.Count / 9 >= _limits.MaxTriangles)
                return MeshErrors.TooManyTriangles(_limits.MaxTriangles);
            if ((triangles.Count & 0x3FFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            int read = 0;
            foreach (Range part in text[6..].Split(' '))
            {
                ReadOnlySpan<char> number = text[6..][part];
                if (number.IsEmpty)
                    continue;
                if (read == 3 || !float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    return MeshErrors.Unreadable("it has an invalid vertex line");
                triangles.Add(value);
                read++;
            }

            if (read != 3)
                return MeshErrors.Unreadable("it has an invalid vertex line");
        }

        if (triangles.Count == 0 || triangles.Count % 9 != 0)
            return MeshErrors.Unreadable(triangles.Count == 0 ? "it has no triangles" : "a facet does not have three vertices");
        return new Mesh([.. triangles], colors: null);
    }

    /// <summary>The bytes already read for the header, then the rest of the stream.</summary>
    private sealed class ConcatStream(byte[] head, Stream tail) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < head.Length)
            {
                int copied = Math.Min(count, head.Length - _position);
                Array.Copy(head, _position, buffer, offset, copied);
                _position += copied;
                return copied;
            }

            return tail.Read(buffer, offset, count);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
