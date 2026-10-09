using System.Globalization;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using SkiaSharp;

namespace Marshal.Artifacts;

/// <summary>
/// Media renditions with SkiaSharp: JPEG, PNG and MP4 as stored, GIF and WebP as a PNG of the first frame, and code,
/// diffs and Markdown as 1920×1080 PNG pages of monospace text (JetBrains Mono, embedded, because the container has
/// no fonts; code highlighted by <see cref="CodeHighlighter"/>). Nothing is cached; text pages render in milliseconds.
/// </summary>
public sealed class SkiaArtifactRenditions(IArtifactStore store) : IArtifactRenditions
{
    /// <summary>Text rows per page; readable on a TV from a distance.</summary>
    internal const int RowsPerPage = 40;

    /// <summary>Text beyond this many bytes is cut, with a final note.</summary>
    internal const int MaxTextBytes = 2 * 1024 * 1024;

    internal const int Width = 1920;
    internal const int Height = 1080;

    private const float Margin = 48;
    private const float FontSize = 20;
    private const float HeaderHeight = 56;
    private const int GutterDigits = 5;

    private static readonly SKTypeface _typeface = LoadTypeface();
    private static readonly SKColor _background = new(0x1e, 0x1e, 0x1e);
    private static readonly SKColor _text = new(0xd4, 0xd4, 0xd4);
    private static readonly SKColor _dim = new(0x80, 0x80, 0x80);
    private static readonly SKColor _added = new(0x6a, 0xd0, 0x7a);
    private static readonly SKColor _removed = new(0xf0, 0x6c, 0x6c);
    private static readonly SKColor _hunk = new(0x5c, 0xb8, 0xd6);
    private static readonly SKColor _string = new(0x55, 0xc9, 0x8f);
    private static readonly SKColor _comment = new(0x8e, 0x97, 0x9d);

    /// <summary>Keyword color; the web viewer's dark-theme accent text.</summary>
    internal static readonly SKColor KeywordColor = new(0xff, 0xa0, 0x61);

    private static readonly float _rowHeight = (Height - 2 * Margin - HeaderHeight) / RowsPerPage;
    private static readonly float _charWidth = MeasureCharWidth();
    private static readonly int _columns = (int)((Width - 2 * Margin) / _charWidth) - GutterDigits - 1;

    public async Task<Result<ArtifactRendition>> DescribeAsync(ArtifactId id, CancellationToken cancellationToken)
    {
        Result<ArtifactRecord> found = await store.GetAsync(id, cancellationToken);
        if (found.IsFailure)
            return found.Error;

        ArtifactRecord artifact = found.Value;
        switch (artifact.Kind)
        {
            case ArtifactKind.Image when artifact.MediaType == "image/jpeg":
                return new ArtifactRendition(artifact, 1, "image/jpeg", "jpg", DisplayMediaType.Image);
            case ArtifactKind.Image when artifact.MediaType is "image/png" or "image/gif" or "image/webp":
                return new ArtifactRendition(artifact, 1, "image/png", "png", DisplayMediaType.Image);
            case ArtifactKind.Video when artifact.MediaType == "video/mp4":
                return new ArtifactRendition(artifact, 1, "video/mp4", "mp4", DisplayMediaType.Video);
            case ArtifactKind.Code or ArtifactKind.Diff or ArtifactKind.Markdown:
                Result<IReadOnlyList<Row>> rows = await ReadRowsAsync(artifact, cancellationToken);
                if (rows.IsFailure)
                    return rows.Error;
                return new ArtifactRendition(artifact, Math.Max(1, (rows.Value.Count + RowsPerPage - 1) / RowsPerPage), "image/png", "png", DisplayMediaType.Image);
            default:
                return NoRendition(artifact.Kind);
        }
    }

    public async Task<Result<Stream>> OpenAsync(ArtifactId id, int page, CancellationToken cancellationToken)
    {
        Result<ArtifactRendition> described = await DescribeAsync(id, cancellationToken);
        if (described.IsFailure)
            return described.Error;

        ArtifactRendition rendition = described.Value;
        if (page < 1 || page > rendition.Pages)
            return PageError(page, rendition.Pages);

        ArtifactRecord artifact = rendition.Artifact;
        if (artifact.Kind is ArtifactKind.Code or ArtifactKind.Diff or ArtifactKind.Markdown)
        {
            Result<IReadOnlyList<Row>> rows = await ReadRowsAsync(artifact, cancellationToken);
            return rows.IsFailure ? rows.Error : Result.Success<Stream>(RenderPage(artifact.Name, rows.Value, page, rendition.Pages));
        }

        Result<(ArtifactRecord Artifact, Stream Content)> opened = await store.OpenReadAsync(id, cancellationToken);
        if (opened.IsFailure)
            return opened.Error;

        if (artifact.MediaType is not ("image/gif" or "image/webp"))
            return Result.Success<Stream>(opened.Value.Content);

        await using Stream content = opened.Value.Content;
        return ConvertToPng(content);
    }

    /// <summary>The page-range error <c>artifacts.show</c> reports too.</summary>
    internal static Error PageError(int page, int pages) =>
        new("artifacts.invalid_arguments", $"Page {page} does not exist; the rendition has {pages} page{(pages == 1 ? "" : "s")}.", ErrorType.Validation);

    private static Error NoRendition(ArtifactKind kind) =>
        new("artifacts.no_rendition",
            $"A display cannot show {kind.ToString().ToLowerInvariant()} artifacts. Displays show images (JPEG, PNG, GIF, WebP), MP4 video, code, diffs and Markdown.",
            ErrorType.Validation);

    private static Result<Stream> ConvertToPng(Stream content)
    {
        using var encoded = SKData.Create(content);
        using SKCodec? codec = encoded is null ? null : SKCodec.Create(encoded);
        using SKBitmap? bitmap = codec is null ? null : SKBitmap.Decode(codec);
        if (bitmap is null)
            return new Error("artifacts.no_rendition", "The image cannot be decoded.", ErrorType.Validation);

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(png.ToArray(), writable: false);
    }

    private async Task<Result<IReadOnlyList<Row>>> ReadRowsAsync(ArtifactRecord artifact, CancellationToken cancellationToken)
    {
        Result<(ArtifactRecord Artifact, Stream Content)> opened = await store.OpenReadAsync(artifact.Id, cancellationToken);
        if (opened.IsFailure)
            return opened.Error;

        byte[] buffer = new byte[MaxTextBytes + 1];
        int read = 0;
        await using (Stream content = opened.Value.Content)
        {
            int count;
            while (read < buffer.Length && (count = await content.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                read += count;
        }

        bool truncated = read > MaxTextBytes;
        string text = Encoding.UTF8.GetString(buffer, 0, Math.Min(read, MaxTextBytes));
        CodeHighlighter.CodeLanguage? language = artifact.Kind == ArtifactKind.Code ? CodeHighlighter.LanguageOf(artifact.Name) : null;
        return Result.Success(Layout(text, artifact.Kind == ArtifactKind.Diff, truncated, _columns, language));
    }

    /// <summary>
    /// Splits text into display rows: tabs as four spaces, long lines wrapped at <paramref name="columns"/>, and with a
    /// <paramref name="language"/> the highlight spans cut to each row.
    /// </summary>
    internal static IReadOnlyList<Row> Layout(string text, bool diff, bool truncated, int columns, CodeHighlighter.CodeLanguage? language = null)
    {
        var rows = new List<Row>();
        string[] split = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int count = split.Length > 1 && split[^1].Length == 0 ? split.Length - 1 : split.Length;
        string[] lines = split.Take(count).Select(line => line.TrimEnd('\r').Replace("\t", "    ", StringComparison.Ordinal)).ToArray();
        IReadOnlyList<IReadOnlyList<CodeHighlighter.CodeSpan>>? highlighted = language is null ? null : CodeHighlighter.Highlight(language, lines);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            RowStyle style = diff ? DiffStyle(line) : RowStyle.Plain;
            int start = 0;
            do
            {
                int length = Math.Min(columns, line.Length - start);
                rows.Add(new Row(start == 0 ? index + 1 : null, line.Substring(start, length), style, Clip(highlighted?[index], start, length)));
                start += length;
            }
            while (start < line.Length);
        }

        if (truncated)
            rows.Add(new Row(null, $"… cut after {MaxTextBytes / (1024 * 1024)} MB", RowStyle.Note, []));

        return rows;
    }

    /// <summary>The spans inside the row <c>[start, start + length)</c>, relative to the row.</summary>
    private static List<CodeHighlighter.CodeSpan> Clip(IReadOnlyList<CodeHighlighter.CodeSpan>? spans, int start, int length)
    {
        if (spans is null)
            return [];

        var clipped = new List<CodeHighlighter.CodeSpan>();
        foreach (CodeHighlighter.CodeSpan span in spans)
        {
            int from = Math.Max(span.Start, start);
            int to = Math.Min(span.Start + span.Length, start + length);
            if (from < to)
                clipped.Add(span with { Start = from - start, Length = to - from });
        }

        return clipped;
    }

    private static RowStyle DiffStyle(string line) => line switch
    {
        _ when line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) => RowStyle.Plain,
        _ when line.StartsWith('+') => RowStyle.Added,
        _ when line.StartsWith('-') => RowStyle.Removed,
        _ when line.StartsWith("@@", StringComparison.Ordinal) => RowStyle.Hunk,
        _ => RowStyle.Plain,
    };

    private static MemoryStream RenderPage(string name, IReadOnlyList<Row> rows, int page, int pages)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        using (var font = new SKFont(_typeface, FontSize))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(_background);
            paint.Color = _dim;
            canvas.DrawText(string.Create(CultureInfo.InvariantCulture, $"{name} — page {page}/{pages}"), Margin, Margin + FontSize, SKTextAlign.Left, font, paint);

            float gutter = Margin + GutterDigits * _charWidth;
            float y = Margin + HeaderHeight + FontSize;
            foreach (Row row in rows.Skip((page - 1) * RowsPerPage).Take(RowsPerPage))
            {
                if (row.Line is { } number)
                {
                    paint.Color = _dim;
                    canvas.DrawText(number.ToString(CultureInfo.InvariantCulture), gutter, y, SKTextAlign.Right, font, paint);
                }

                SKColor color = row.Style switch
                {
                    RowStyle.Added => _added,
                    RowStyle.Removed => _removed,
                    RowStyle.Hunk => _hunk,
                    RowStyle.Note => _dim,
                    _ => _text,
                };
                DrawRow(canvas, font, paint, row, gutter + _charWidth, y, color);
                y += _rowHeight;
            }
        }

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(png.ToArray(), writable: false);
    }

    /// <summary>Draws the row in segments: plain text in <paramref name="color"/>, each span in its token color.</summary>
    private static void DrawRow(SKCanvas canvas, SKFont font, SKPaint paint, Row row, float x, float y, SKColor color)
    {
        int column = 0;
        foreach (CodeHighlighter.CodeSpan span in row.Spans)
        {
            Draw(row.Text[column..span.Start], column, color);
            Draw(row.Text.Substring(span.Start, span.Length), span.Start, span.Token switch
            {
                CodeHighlighter.CodeToken.Keyword => KeywordColor,
                CodeHighlighter.CodeToken.Comment => _comment,
                _ => _string,
            });
            column = span.Start + span.Length;
        }

        Draw(row.Text[column..], column, color);

        void Draw(string text, int at, SKColor segmentColor)
        {
            if (text.Length == 0)
                return;
            paint.Color = segmentColor;
            canvas.DrawText(text, x + at * _charWidth, y, SKTextAlign.Left, font, paint);
        }
    }

    private static SKTypeface LoadTypeface()
    {
        using Stream font = typeof(SkiaArtifactRenditions).Assembly.GetManifestResourceStream("Marshal.Artifacts.Fonts.JetBrainsMono-Regular.ttf")
            ?? throw new InvalidOperationException("The embedded monospace font is missing.");
        return SKTypeface.FromStream(font) ?? throw new InvalidOperationException("The embedded monospace font cannot be read.");
    }

    private static float MeasureCharWidth()
    {
        using var font = new SKFont(_typeface, FontSize);
        return font.MeasureText("M");
    }

    /// <summary>One display row: the source line number on its first row, the text, its color and highlight spans.</summary>
    internal sealed record Row(int? Line, string Text, RowStyle Style, IReadOnlyList<CodeHighlighter.CodeSpan> Spans);

    internal enum RowStyle
    {
        Plain,
        Added,
        Removed,
        Hunk,
        Note,
    }
}
