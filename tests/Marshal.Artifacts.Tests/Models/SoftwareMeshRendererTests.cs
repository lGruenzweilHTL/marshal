using System.Numerics;
using Marshal.Artifacts.Models;
using SkiaSharp;

namespace Marshal.Artifacts.Tests.Models;

public sealed class SoftwareMeshRendererTests
{
    private static readonly SoftwareMeshRenderer _renderer = new();

    [Fact]
    public void Main_CenterShowsModelInDefaultColor()
    {
        using SKBitmap image = Render(MeshFixtures.Cube(), MeshView.Main, 320, 180);

        SKColor center = image.GetPixel(160, 90);
        Assert.NotEqual(SoftwareMeshRenderer.Background, center);
        Assert.True(center.Red > center.Green && center.Green > center.Blue, $"expected a shade of orange, got {center}");
        Assert.Equal(SoftwareMeshRenderer.Background, image.GetPixel(2, 2));
    }

    [Fact]
    public void NearerTriangleWins()
    {
        // Two squares facing the front camera (which looks along +Y): red at y = 0, blue behind it at y = 10.
        Vector3[][] near = Square(0);
        Vector3[][] far = Square(10);
        var mesh = new Mesh([.. far.Concat(near).SelectMany(t => t).SelectMany(v => (float[])[v.X, v.Y, v.Z])],
            [0xFF0000FF, 0xFF0000FF, 0xFFFF0000, 0xFFFF0000]);

        using SKBitmap image = _renderer.Render(mesh, MeshView.Front, 200, 200);

        SKColor center = image.GetPixel(100, 100);
        Assert.True(center.Red > 100 && center.Blue < 50, $"expected red, got {center}");
    }

    [Theory]
    [InlineData("Front", 4.0)]
    [InlineData("Right", 2.0)]
    [InlineData("Top", 2.0)]
    public void OrthographicViews_ShowTheMatchingSides(string viewName, double ratio)
    {
        MeshView view = Enum.Parse<MeshView>(viewName);
        // 40 wide (x), 20 deep (y), 10 high (z).
        using SKBitmap image = Render(MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10)), view, 400, 400);

        (int width, int height) = Extent(image);
        Assert.InRange(width / (double)height, ratio * 0.9, ratio * 1.1);
    }

    [Fact]
    public void OrthographicViews_ShareOneScale()
    {
        Vector3[][] box = MeshFixtures.Box(Vector3.Zero, new Vector3(40, 20, 10));
        using SKBitmap front = Render(box, MeshView.Front, 400, 400);
        using SKBitmap top = Render(box, MeshView.Top, 400, 400);

        Assert.InRange(Extent(front).Width, Extent(top).Width - 2, Extent(top).Width + 2);
    }

    [Fact]
    public void Color_IsShaded()
    {
        var mesh = new Mesh([.. MeshFixtures.Cube().SelectMany(t => t).SelectMany(v => (float[])[v.X, v.Y, v.Z])], [.. Enumerable.Repeat(0xFF2040FFu, 12)]);

        using SKBitmap image = _renderer.Render(mesh, MeshView.Main, 320, 180);

        SKColor center = image.GetPixel(160, 90);
        Assert.True(center.Blue > center.Red && center.Blue > center.Green && center.Blue <= 0xFF, $"expected a shade of blue, got {center}");
    }

    [Fact]
    public void FlatModel_StillVisible()
    {
        // A single triangle in the XY plane: zero height must not break the fit.
        var mesh = new Mesh([0, 0, 0, 10, 0, 0, 0, 10, 0], null);

        using SKBitmap image = _renderer.Render(mesh, MeshView.Top, 100, 100);

        Assert.True(Extent(image).Width > 50);
    }

    private static SKBitmap Render(Vector3[][] triangles, MeshView view, int width, int height) =>
        _renderer.Render(new Mesh([.. triangles.SelectMany(t => t).SelectMany(v => (float[])[v.X, v.Y, v.Z])], null), view, width, height);

    private static Vector3[][] Square(float y) =>
        [[new(0, y, 0), new(10, y, 0), new(10, y, 10)], [new(0, y, 0), new(10, y, 10), new(0, y, 10)]];

    private static (int Width, int Height) Extent(SKBitmap image)
    {
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y) == SoftwareMeshRenderer.Background)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return (right - left + 1, bottom - top + 1);
    }
}
