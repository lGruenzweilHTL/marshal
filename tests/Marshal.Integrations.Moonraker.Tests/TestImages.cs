using SkiaSharp;

namespace Marshal.Integrations.Moonraker.Tests;

internal static class TestImages
{
    /// <summary>A solid-color image of the given size in the given format.</summary>
    public static byte[] Create(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.OrangeRed);
        using SKData data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    public static (int Width, int Height) Size(byte[] image)
    {
        using var codec = SKCodec.Create(new MemoryStream(image));
        return (codec.Info.Width, codec.Info.Height);
    }

    public static bool IsJpeg(byte[] image) => image.Length > 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF;
}
