using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Marshal.Abstractions.Artifacts;

namespace Marshal.Artifacts;

/// <summary>A detected kind with its media type.</summary>
internal sealed record DetectedKind(ArtifactKind Kind, string MediaType)
{
    public static DetectedKind Other { get; } = new(ArtifactKind.Other, "application/octet-stream");
}

/// <summary>Kind and media type of a stored file.</summary>
internal interface IArtifactKindDetector
{
    /// <summary>Detects from <paramref name="name"/>'s extension and the content; <paramref name="content"/> is seekable and read from the start.</summary>
    DetectedKind Detect(string name, Stream content);
}

/// <summary>
/// The extension names a candidate kind, and the content must agree: magic bytes for binary formats, the STL size
/// rule or <c>solid</c> header, a ZIP with <c>3D/3dmodel.model</c> for 3MF, valid UTF-8 without NUL bytes for text
/// kinds and an <c>&lt;svg</c> root for SVG. A mismatch is <see cref="ArtifactKind.Other"/>, so a declared media type
/// or a misleading name never decides how a file is served.
/// </summary>
internal sealed class ArtifactKindDetector : IArtifactKindDetector
{
    /// <summary>Source and text extensions stored as <see cref="ArtifactKind.Code"/>; the viewer picks the highlighter from the extension.</summary>
    public static IReadOnlySet<string> CodeExtensions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "cs", "csproj", "props", "ts", "tsx", "js", "jsx", "mjs", "cjs", "vue", "py", "scad", "yaml", "yml", "json", "toml", "ini", "cfg",
        "conf", "sh", "bash", "ps1", "c", "h", "cpp", "hpp", "rs", "go", "java", "kt", "swift", "rb", "php", "sql", "css", "scss",
        "xml", "gcode", "txt", "log", "csv", "dockerfile",
    };

    private static readonly Dictionary<string, (ArtifactKind Kind, string MediaType)> _byExtension = new(StringComparer.Ordinal)
    {
        ["png"] = (ArtifactKind.Image, "image/png"),
        ["jpg"] = (ArtifactKind.Image, "image/jpeg"),
        ["jpeg"] = (ArtifactKind.Image, "image/jpeg"),
        ["gif"] = (ArtifactKind.Image, "image/gif"),
        ["webp"] = (ArtifactKind.Image, "image/webp"),
        ["svg"] = (ArtifactKind.Svg, "image/svg+xml"),
        ["mp4"] = (ArtifactKind.Video, "video/mp4"),
        ["webm"] = (ArtifactKind.Video, "video/webm"),
        ["stl"] = (ArtifactKind.Model3d, "model/stl"),
        ["3mf"] = (ArtifactKind.Model3d, "model/3mf"),
        ["pdf"] = (ArtifactKind.Pdf, "application/pdf"),
        ["md"] = (ArtifactKind.Markdown, "text/markdown; charset=utf-8"),
        ["markdown"] = (ArtifactKind.Markdown, "text/markdown; charset=utf-8"),
        ["html"] = (ArtifactKind.Html, "text/html; charset=utf-8"),
        ["htm"] = (ArtifactKind.Html, "text/html; charset=utf-8"),
        ["diff"] = (ArtifactKind.Diff, "text/x-diff; charset=utf-8"),
        ["patch"] = (ArtifactKind.Diff, "text/x-diff; charset=utf-8"),
    };

    public DetectedKind Detect(string name, Stream content)
    {
        string extension = Extension(name);
        (ArtifactKind Kind, string MediaType) candidate = _byExtension.TryGetValue(extension, out (ArtifactKind Kind, string MediaType) known)
            ? known
            : CodeExtensions.Contains(extension) ? (ArtifactKind.Code, "text/plain; charset=utf-8") : (ArtifactKind.Other, DetectedKind.Other.MediaType);
        if (candidate.Kind == ArtifactKind.Other)
            return DetectedKind.Other;

        content.Position = 0;
        bool matches = candidate.Kind switch
        {
            ArtifactKind.Image => Image(Head(content), extension),
            ArtifactKind.Video => Video(Head(content), extension),
            ArtifactKind.Pdf => Head(content).AsSpan().StartsWith("%PDF-"u8),
            ArtifactKind.Model3d => extension == "stl" ? Stl(content) : ThreeMf(content),
            ArtifactKind.Svg => Text(content) is { } svg && SvgRoot(svg),
            _ => Text(content) is not null,
        };
        content.Position = 0;
        return matches ? new DetectedKind(candidate.Kind, candidate.MediaType) : DetectedKind.Other;
    }

    private static string Extension(string name)
    {
        string file = Path.GetFileName(name).ToLowerInvariant();
        if (file == "dockerfile")
            return file;
        int dot = file.LastIndexOf('.');
        return dot < 0 ? "" : file[(dot + 1)..];
    }

    private static byte[] Head(Stream content)
    {
        byte[] head = new byte[16];
        int read = content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return head[..read];
    }

    private static bool Image(ReadOnlySpan<byte> head, string extension) => extension switch
    {
        "png" => head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "jpg" or "jpeg" => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        "gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
        "webp" => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        _ => false,
    };

    private static bool Video(ReadOnlySpan<byte> head, string extension) => extension switch
    {
        "mp4" => head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8),
        "webm" => head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]),
        _ => false,
    };

    // Binary STL: 80-byte header, triangle count, 50 bytes per triangle. ASCII STL: "solid" and text with facets.
    private static bool Stl(Stream content)
    {
        long length = content.Length;
        if (length >= 84)
        {
            byte[] header = new byte[84];
            content.ReadExactly(header);
            uint triangles = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
            if (84 + (50L * triangles) == length)
                return true;
        }

        content.Position = 0;
        return Text(content) is { } text && text.TrimStart().StartsWith("solid", StringComparison.Ordinal) && text.Contains("facet", StringComparison.Ordinal);
    }

    private static bool ThreeMf(Stream content)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            return zip.Entries.Any(entry => string.Equals(entry.FullName, "3D/3dmodel.model", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // Whole content as strict UTF-8 without NUL bytes; null for anything else.
    private static string? Text(Stream content)
    {
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        using var reader = new StreamReader(content, decoder, detectEncodingFromByteOrderMarks: false, bufferSize: 81920, leaveOpen: true);
        try
        {
            string text = reader.ReadToEnd();
            return text.Contains('\0', StringComparison.Ordinal) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // The first element after the XML declaration, comments and a doctype must be <svg.
    private static bool SvgRoot(string text)
    {
        int position = 0;
        while (true)
        {
            position = text.IndexOf('<', position);
            if (position < 0)
                return false;
            if (string.CompareOrdinal(text, position, "<!--", 0, 4) == 0)
            {
                int end = text.IndexOf("-->", position, StringComparison.Ordinal);
                if (end < 0)
                    return false;
                position = end + 3;
            }
            else if (position + 1 < text.Length && text[position + 1] is '?' or '!')
            {
                int end = text.IndexOf('>', position);
                if (end < 0)
                    return false;
                position = end + 1;
            }
            else
            {
                return string.CompareOrdinal(text, position, "<svg", 0, 4) == 0 && (position + 4 == text.Length || !char.IsLetterOrDigit(text[position + 4]));
            }
        }
    }
}
