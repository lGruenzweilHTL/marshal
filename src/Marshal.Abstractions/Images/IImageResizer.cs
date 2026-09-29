namespace Marshal.Abstractions.Images;

/// <summary>
/// Scales images down for storage and vision models.
/// </summary>
public interface IImageResizer
{
    /// <summary>
    /// Decodes <paramref name="image"/>, scales it so its longest side is at most <paramref name="maxSize"/> pixels
    /// (never up) and encodes it as JPEG; <c>image.invalid</c> when it cannot be decoded.
    /// </summary>
    Result<ResizedImage> ToJpeg(ReadOnlySpan<byte> image, int maxSize, int quality);
}

/// <summary>A JPEG and its size.</summary>
public sealed record ResizedImage(byte[] Jpeg, int Width, int Height);
