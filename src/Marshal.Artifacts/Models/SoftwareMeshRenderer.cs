using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marshal.Artifacts.Models;

/// <summary>Camera of a model picture: the shaded perspective view from the front-right above, or one side.</summary>
internal enum MeshView
{
    Main,
    Front,
    Right,
    Top,
}

/// <summary>Draws a mesh into a bitmap; the caller disposes it.</summary>
internal interface IMeshRenderer
{
    SKBitmap Render(Mesh mesh, MeshView view, int width, int height);
}

/// <summary>
/// A z-buffer rasterizer on the CPU (the container has no GPU): flat shading by the angle to a fixed light, both sides
/// lit (open meshes and mixed winding are common), darkened silhouette and crease edges, 2× supersampling. Side views
/// are orthographic with one scale (the model's largest dimension), so their sizes compare; the main view is a
/// perspective fitted to the picture.
/// </summary>
internal sealed class SoftwareMeshRenderer : IMeshRenderer
{
    /// <summary>The dark page background of display renditions.</summary>
    public static readonly SKColor Background = new(0x1e, 0x1e, 0x1e);

    /// <summary>The dark theme's accent, for models without colors.</summary>
    public static readonly SKColor DefaultColor = new(0xff, 0x8a, 0x3d);

    private const int Supersample = 2;
    private const float Margin = 0.08f;
    private const float Ambient = 0.35f;
    private const float EdgeShade = 0.55f;
    private static readonly float _azimuth = 35f * MathF.PI / 180f;
    private static readonly float _elevation = 30f * MathF.PI / 180f;
    private static readonly float _fieldOfView = 30f * MathF.PI / 180f;

    public SKBitmap Render(Mesh mesh, MeshView view, int width, int height)
    {
        int w = width * Supersample;
        int h = height * Supersample;
        (Vector3 right, Vector3 up, Vector3 back) = Camera(view);
        Vector3 center = (mesh.Min + mesh.Max) / 2;
        float radius = Math.Max(mesh.Size.Length() / 2, 1e-3f);
        float distance = radius / MathF.Sin(_fieldOfView / 2);
        bool perspective = view == MeshView.Main;

        // Project every corner: screen x/y before fitting, and depth (smaller is nearer).
        float[] triangles = mesh.Triangles;
        int corners = triangles.Length / 3;
        float[] sx = new float[corners];
        float[] sy = new float[corners];
        float[] depth = new float[corners];
        Parallel.For(0, corners, i =>
        {
            Vector3 d = new Vector3(triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]) - center;
            float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up), z = Vector3.Dot(d, back);
            if (perspective)
            {
                float wDistance = Math.Max(distance - z, 1e-3f);
                (sx[i], sy[i], depth[i]) = (x / wDistance, y / wDistance, wDistance);
            }
            else
            {
                (sx[i], sy[i], depth[i]) = (x, y, -z);
            }
        });

        (float scale, float cx, float cy) = Fit(mesh, sx, sy, w, h, perspective);
        for (int i = 0; i < corners; i++)
        {
            sx[i] = w / 2f + (sx[i] - cx) * scale;
            sy[i] = h / 2f - (sy[i] - cy) * scale;
        }

        uint[] shades = Shades(mesh, right, up, back);
        uint[] color = new uint[w * h];
        float[] zbuffer = new float[w * h];
        Array.Fill(color, (uint)Background);
        Array.Fill(zbuffer, float.PositiveInfinity);

        // Bands of rows in parallel; each band walks every triangle and fills only its own rows.
        int bands = Math.Max(1, Math.Min(h, Environment.ProcessorCount * 4));
        Parallel.For(0, bands, band =>
        {
            int top = band * h / bands, bottom = (band + 1) * h / bands;
            for (int t = 0; t < shades.Length; t++)
            {
                if (shades[t] != 0)
                    Fill(t, top, bottom);
            }
        });

        void Fill(int t, int bandTop, int bandBottom)
        {
            int a = 3 * t, b = a + 1, c = a + 2;
            float area = Edge(sx[a], sy[a], sx[b], sy[b], sx[c], sy[c]);
            if (MathF.Abs(area) < 1e-6f)
                return;

            int minX = Math.Max(0, (int)MathF.Floor(Math.Min(sx[a], Math.Min(sx[b], sx[c]))));
            int maxX = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(sx[a], Math.Max(sx[b], sx[c]))));
            int minY = Math.Max(bandTop, (int)MathF.Floor(Math.Min(sy[a], Math.Min(sy[b], sy[c]))));
            int maxY = Math.Min(bandBottom - 1, (int)MathF.Ceiling(Math.Max(sy[a], Math.Max(sy[b], sy[c]))));
            float inverse = 1f / area;
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float w0 = Edge(sx[b], sy[b], sx[c], sy[c], px, py) * inverse;
                    float w1 = Edge(sx[c], sy[c], sx[a], sy[a], px, py) * inverse;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                        continue;

                    float z = w0 * depth[a] + w1 * depth[b] + w2 * depth[c];
                    int index = y * w + x;
                    if (z < zbuffer[index])
                    {
                        zbuffer[index] = z;
                        color[index] = shades[t];
                    }
                }
            }
        }

        DarkenEdges(color, zbuffer, w, h, depth);
        return Downsample(color, w, width, height);
    }

    private static (Vector3 Right, Vector3 Up, Vector3 Back) Camera(MeshView view)
    {
        Vector3 back = view switch
        {
            MeshView.Front => -Vector3.UnitY,
            MeshView.Right => Vector3.UnitX,
            MeshView.Top => Vector3.UnitZ,
            _ => new Vector3(MathF.Cos(_elevation) * MathF.Sin(_azimuth), -MathF.Cos(_elevation) * MathF.Cos(_azimuth), MathF.Sin(_elevation)),
        };
        Vector3 right = view == MeshView.Top ? Vector3.UnitX : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, back));
        var up = Vector3.Cross(back, right);
        return (right, up, back);
    }

    private static (float Scale, float CenterX, float CenterY) Fit(Mesh mesh, float[] sx, float[] sy, int w, int h, bool perspective)
    {
        float usableW = w * (1 - 2 * Margin), usableH = h * (1 - 2 * Margin);
        if (!perspective)
        {
            float largest = Math.Max(mesh.Size.X, Math.Max(mesh.Size.Y, mesh.Size.Z));
            return (Math.Min(usableW, usableH) / Math.Max(largest, 1e-3f), 0, 0);
        }

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < sx.Length; i++)
        {
            minX = Math.Min(minX, sx[i]);
            maxX = Math.Max(maxX, sx[i]);
            minY = Math.Min(minY, sy[i]);
            maxY = Math.Max(maxY, sy[i]);
        }

        float scale = Math.Min(usableW / Math.Max(maxX - minX, 1e-6f), usableH / Math.Max(maxY - minY, 1e-6f));
        return (scale, (minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>The shaded color of each triangle; 0 for degenerate triangles, which are skipped.</summary>
    private static uint[] Shades(Mesh mesh, Vector3 right, Vector3 up, Vector3 back)
    {
        var light = Vector3.Normalize(-0.35f * right + 0.55f * up + 0.75f * back);
        float[] t = mesh.Triangles;
        uint[] shades = new uint[mesh.TriangleCount];
        Parallel.For(0, shades.Length, i =>
        {
            int o = 9 * i;
            var a = new Vector3(t[o], t[o + 1], t[o + 2]);
            var normal = Vector3.Cross(new Vector3(t[o + 3], t[o + 4], t[o + 5]) - a, new Vector3(t[o + 6], t[o + 7], t[o + 8]) - a);
            float length = normal.Length();
            if (length < 1e-12f)
                return;

            float intensity = Ambient + (1 - Ambient) * MathF.Abs(Vector3.Dot(normal / length, light));
            uint argb = mesh.Colors is { } colors && colors[i] != 0 ? colors[i] : (uint)DefaultColor;
            shades[i] = Scale(argb, intensity) | 0xFF000000;
        });
        return shades;
    }

    /// <summary>Silhouettes and depth jumps get darker so shapes read at TV distance.</summary>
    private static void DarkenEdges(uint[] color, float[] zbuffer, int w, int h, float[] depth)
    {
        float near = float.MaxValue, far = float.MinValue;
        foreach (float z in depth)
        {
            near = Math.Min(near, z);
            far = Math.Max(far, z);
        }

        float jump = Math.Max(far - near, 1e-6f) * 0.02f;
        bool[] edges = new bool[w * h];
        Parallel.For(1, h - 1, y =>
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float z = zbuffer[i];
                if (float.IsPositiveInfinity(z))
                    continue;
                edges[i] = Jump(zbuffer[i - 1]) || Jump(zbuffer[i + 1]) || Jump(zbuffer[i - w]) || Jump(zbuffer[i + w]);

                bool Jump(float other) => float.IsPositiveInfinity(other) || MathF.Abs(other - z) > jump;
            }
        });

        for (int i = 0; i < edges.Length; i++)
        {
            if (edges[i])
                color[i] = Scale(color[i], EdgeShade) | 0xFF000000;
        }
    }

    private static SKBitmap Downsample(uint[] color, int w, int width, int height)
    {
        uint[] pixels = new uint[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                uint r = 0, g = 0, b = 0;
                for (int dy = 0; dy < Supersample; dy++)
                {
                    for (int dx = 0; dx < Supersample; dx++)
                    {
                        uint c = color[(y * Supersample + dy) * w + x * Supersample + dx];
                        r += (c >> 16) & 0xFF;
                        g += (c >> 8) & 0xFF;
                        b += c & 0xFF;
                    }
                }

                const int samples = Supersample * Supersample;
                pixels[y * width + x] = 0xFF000000 | ((r / samples) << 16) | ((g / samples) << 8) | (b / samples);
            }
        });

        // Bgra8888 in memory is 0xAARRGGBB as a little-endian uint, the same layout as SKColor.
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(MemoryMarshal.Cast<uint, int>(pixels).ToArray(), 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    private static uint Scale(uint argb, float factor)
    {
        uint r = (uint)Math.Min(255f, ((argb >> 16) & 0xFF) * factor);
        uint g = (uint)Math.Min(255f, ((argb >> 8) & 0xFF) * factor);
        uint b = (uint)Math.Min(255f, (argb & 0xFF) * factor);
        return (r << 16) | (g << 8) | b;
    }
}
