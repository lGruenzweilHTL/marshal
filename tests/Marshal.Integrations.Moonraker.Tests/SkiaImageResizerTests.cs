using Marshal.Abstractions.Images;
using SkiaSharp;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class SkiaImageResizerTests
{
    [Fact]
    public void LargeImage_ScaledToTheLongestSide()
    {
        ResizedImage resized = new SkiaImageResizer().ToJpeg(TestImages.Create(2000, 1000, SKEncodedImageFormat.Png), 1024, 85).Value;

        Assert.Equal((1024, 512), (resized.Width, resized.Height));
        Assert.Equal((1024, 512), TestImages.Size(resized.Jpeg));
        Assert.True(TestImages.IsJpeg(resized.Jpeg));
    }

    [Fact]
    public void PortraitImage_ScaledByHeight()
    {
        ResizedImage resized = new SkiaImageResizer().ToJpeg(TestImages.Create(600, 1200), 300, 85).Value;

        Assert.Equal((150, 300), (resized.Width, resized.Height));
    }

    [Fact]
    public void SmallImage_NotEnlarged()
    {
        ResizedImage resized = new SkiaImageResizer().ToJpeg(TestImages.Create(320, 240), 1024, 85).Value;

        Assert.Equal((320, 240), TestImages.Size(resized.Jpeg));
    }

    [Fact]
    public void Garbage_Invalid()
    {
        Assert.Equal("image.invalid", new SkiaImageResizer().ToJpeg("not an image"u8, 1024, 85).Error.Code);
    }
}
