using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Artifacts.Models;
using SkiaSharp;

namespace Marshal.Artifacts;

/// <summary>
/// 3D model renditions and previews: page 1 is the shaded perspective view, page 2 four views (front, right, top,
/// perspective), both with the size in millimetres in the header. Pages and previews are cached by file hash, after
/// the owner-scoped artifact lookup.
/// </summary>
public sealed partial class SkiaArtifactRenditions
{
    internal const int ModelPages = 2;
    internal const int PreviewWidth = 480;
    internal const int PreviewHeight = 270;

    private const float TileGap = 16;
    private const float TileLabel = 32;

    private static readonly TimeSpan _modelBudget = TimeSpan.FromSeconds(30);
    private static readonly SoftwareMeshRenderer _renderer = new();
    private static readonly (MeshView View, string Label)[] _sheet = [(MeshView.Front, "Front"), (MeshView.Right, "Right"), (MeshView.Top, "Top"), (MeshView.Main, "Perspective")];

    public async Task<Result<ArtifactPreview>> GetPreviewAsync(ArtifactId id, CancellationToken cancellationToken)
    {
        Result<ArtifactRecord> found = await store.GetAsync(id, cancellationToken);
        if (found.IsFailure)
            return found.Error;

        ArtifactRecord artifact = found.Value;
        Result<byte[]> png = artifact.Kind switch
        {
            ArtifactKind.Model3d => await CachedAsync(artifact, "preview", Preview, cancellationToken),
            ArtifactKind.Image => await ImagePreviewAsync(artifact, cancellationToken),
            _ => NoPreview($"{artifact.Kind.ToString().ToLowerInvariant()} artifacts have no preview"),
        };
        return png.IsFailure
            ? png.Error.Type == ErrorType.NotFound ? png.Error : NoPreview(png.Error.Message)
            : new ArtifactPreview(png.Value, artifact.Sha256);
    }

    /// <summary>The header of a model page: name, page and bounding box size.</summary>
    internal static string ModelHeader(string name, Mesh mesh, int page) => string.Create(CultureInfo.InvariantCulture,
        $"{name} — page {page}/{ModelPages} — {mesh.Size.X:F1} × {mesh.Size.Y:F1} × {mesh.Size.Z:F1} mm");

    /// <summary>Centers of the four views on page 2, in the order front, right, top, perspective.</summary>
    internal static IEnumerable<(int X, int Y)> TileCenters() =>
        Tiles().Select(tile => ((int)tile.MidX, (int)(tile.Top + TileLabel + (tile.Height - TileLabel) / 2)));

    private Task<Result<byte[]>> ModelPageAsync(ArtifactRecord artifact, int page, CancellationToken cancellationToken) =>
        CachedAsync(artifact, $"p{page.ToString(CultureInfo.InvariantCulture)}", mesh => page == 1 ? DrawMain(artifact.Name, mesh) : DrawSheet(artifact.Name, mesh), cancellationToken);

    private async Task<Result<byte[]>> CachedAsync(ArtifactRecord artifact, string variant, Func<Mesh, byte[]> draw, CancellationToken cancellationToken)
    {
        string? key = artifact.Sha256 is { Length: > 0 } sha ? $"{sha}-{variant}" : null;
        if (key is not null && await _cache.GetAsync(key, cancellationToken) is { } cached)
            return cached;

        Result<Mesh> mesh = await ReadMeshAsync(artifact, cancellationToken);
        if (mesh.IsFailure)
            return mesh.Error;

        byte[] png = draw(mesh.Value);
        if (key is not null)
            await _cache.SetAsync(key, png, cancellationToken);
        return png;
    }

    private async Task<Result<Mesh>> ReadMeshAsync(ArtifactRecord artifact, CancellationToken cancellationToken)
    {
        Result<(ArtifactRecord Artifact, Stream Content)> opened = await store.OpenReadAsync(artifact.Id, cancellationToken);
        if (opened.IsFailure)
            return opened.Error;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_modelBudget);
        await using Stream content = opened.Value.Content;
        IMeshReader reader = artifact.MediaType == "model/3mf" ? new ThreeMfMeshReader() : new StlMeshReader();
        try
        {
            // The readers need a seekable stream (STL size rule, zip directory); stored files are, others are copied.
            Stream seekable = content.CanSeek ? content : await CopyAsync(content, budget.Token);
            return await Task.Run(() => reader.Read(seekable, budget.Token), budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return MeshErrors.Unreadable("it takes too long to read");
        }
    }

    private static async Task<Stream> CopyAsync(Stream content, CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    private static byte[] DrawMain(string name, Mesh mesh)
    {
        using var page = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(page))
        {
            DrawHeader(canvas, ModelHeader(name, mesh, 1));
            float top = Margin + HeaderHeight;
            using SKBitmap view = _renderer.Render(mesh, MeshView.Main, (int)(Width - 2 * Margin), (int)(Height - top - Margin));
            using var image = SKImage.FromBitmap(view);
            canvas.DrawImage(image, Margin, top, SKSamplingOptions.Default);
        }

        return Encode(page);
    }

    private static byte[] DrawSheet(string name, Mesh mesh)
    {
        using var page = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(page))
        using (var font = new SKFont(_typeface, FontSize))
        using (var paint = new SKPaint { IsAntialias = true, Color = _dim })
        {
            DrawHeader(canvas, ModelHeader(name, mesh, 2));
            foreach ((SKRect tile, (MeshView view, string label)) in Tiles().Zip(_sheet))
            {
                canvas.DrawText(label, tile.Left, tile.Top + FontSize, SKTextAlign.Left, font, paint);
                using SKBitmap rendered = _renderer.Render(mesh, view, (int)tile.Width, (int)(tile.Height - TileLabel));
                using var image = SKImage.FromBitmap(rendered);
                canvas.DrawImage(image, tile.Left, tile.Top + TileLabel, SKSamplingOptions.Default);
            }
        }

        return Encode(page);
    }

    private static void DrawHeader(SKCanvas canvas, string text)
    {
        using var font = new SKFont(_typeface, FontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = _dim };
        canvas.Clear(_background);
        canvas.DrawText(text, Margin, Margin + FontSize, SKTextAlign.Left, font, paint);
    }

    private static SKRect[] Tiles()
    {
        float left = Margin, top = Margin + HeaderHeight;
        float width = (Width - 2 * Margin - TileGap) / 2, height = (Height - top - Margin - TileGap) / 2;
        return
        [
            SKRect.Create(left, top, width, height),
            SKRect.Create(left + width + TileGap, top, width, height),
            SKRect.Create(left, top + height + TileGap, width, height),
            SKRect.Create(left + width + TileGap, top + height + TileGap, width, height),
        ];
    }

    private async Task<Result<byte[]>> ImagePreviewAsync(ArtifactRecord artifact, CancellationToken cancellationToken)
    {
        Result<(ArtifactRecord Artifact, Stream Content)> opened = await store.OpenReadAsync(artifact.Id, cancellationToken);
        if (opened.IsFailure)
            return opened.Error;

        await using Stream content = opened.Value.Content;
        using var data = SKData.Create(content);
        using SKBitmap? image = data is null ? null : SKBitmap.Decode(data);
        if (image is null)
            return NoPreview("the image cannot be decoded");

        float scale = Math.Min(1f, Math.Min(PreviewWidth / (float)image.Width, PreviewHeight / (float)image.Height));
        if (scale >= 1f)
            return Encode(image);

        using SKBitmap scaled = image.Resize(new SKImageInfo(Math.Max(1, (int)MathF.Round(image.Width * scale)), Math.Max(1, (int)MathF.Round(image.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return Encode(scaled);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static byte[] Preview(Mesh mesh)
    {
        using SKBitmap view = _renderer.Render(mesh, MeshView.Main, PreviewWidth, PreviewHeight);
        return Encode(view);
    }

    private static Error NoPreview(string reason) => new("artifacts.no_preview", $"No preview: {reason}.", ErrorType.NotFound);
}
