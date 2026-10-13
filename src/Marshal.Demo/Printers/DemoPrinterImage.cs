using SkiaSharp;

namespace Marshal.Demo.Printers;

/// <summary>
/// Draws the simulated webcam picture: the print bed seen from the front, with the part as tall as the print
/// progress. Good enough for the printer view, snapshots and the vision checks to have something to look at.
/// </summary>
internal static class DemoPrinterImage
{
    public const int Width = 640;
    public const int Height = 480;

    public static byte[] Jpeg(double progress, string state)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x1E, 0x22, 0x27));

        using var enclosure = new SKPaint { Color = new SKColor(0x2C, 0x32, 0x38), IsAntialias = true };
        canvas.DrawRect(40, 40, Width - 80, Height - 80, enclosure);

        using var bed = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x3A), IsAntialias = true };
        canvas.DrawRect(90, 360, Width - 180, 28, bed);
        using var sheet = new SKPaint { Color = new SKColor(0xB8, 0x8A, 0x3E), IsAntialias = true };
        canvas.DrawRect(90, 352, Width - 180, 10, sheet);

        // Two planter brackets: an L shape that grows layer by layer.
        float height = (float)(180 * Math.Clamp(progress, 0, 1));
        using var part = new SKPaint { Color = new SKColor(0x18, 0x18, 0x1A), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0x55, 0x5B, 0x63), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        foreach (float left in new[] { 200f, 360f })
        {
            canvas.DrawRect(left, 352 - Math.Min(height, 40), 90, Math.Min(height, 40), part);
            if (height > 40)
                canvas.DrawRect(left, 352 - height, 26, height - 40, part);
            canvas.DrawRect(left, 352 - height, 90, height, edge);
        }

        // The toolhead above the current layer while printing.
        if (state is "printing" or "paused")
        {
            using var head = new SKPaint { Color = new SKColor(0xC8, 0x3B, 0x26), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(250, 300 - height - 46, 340, 300 - height + 4), 6), head);
            using var nozzle = new SKPaint { Color = new SKColor(0xD4, 0xA0, 0x3A), IsAntialias = true };
            canvas.DrawRect(290, 300 - height + 4, 10, 40, nozzle);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }
}
