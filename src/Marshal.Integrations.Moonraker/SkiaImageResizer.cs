using Marshal.Abstractions;
using Marshal.Abstractions.Images;
using SkiaSharp;

namespace Marshal.Integrations.Moonraker;

/// <summary>Resizes with SkiaSharp (high-quality cubic sampling) and encodes JPEG.</summary>
public sealed class SkiaImageResizer : IImageResizer
{
    private static readonly SKSamplingOptions _sampling = new(SKCubicResampler.Mitchell);

    public Result<ResizedImage> ToJpeg(ReadOnlySpan<byte> image, int maxSize, int quality)
    {
        using var encoded = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(encoded);
        using SKBitmap? source = codec is null ? null : SKBitmap.Decode(codec);
        if (source is null)
            return new Error("image.invalid", "The image could not be decoded.", ErrorType.Validation);

        double scale = Math.Min(1, (double)maxSize / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using SKBitmap scaled = scale < 1 ? source.Resize(new SKImageInfo(width, height), _sampling) : source.Copy();
        using SKData data = scaled.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return new ResizedImage(data.ToArray(), width, height);
    }
}
