using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Kontur;

internal sealed class AmbientWall : IDisposable
{
    private const int Width = 176;
    private const int Height = 112;
    private readonly Bitmap texture = new(Width, Height, PixelFormat.Format32bppArgb);
    private readonly int[] buffer = new int[Width * Height];
    private RectangleF lastStage;
    private RectangleF lastRail;
    private Rgb[]? lastFrame;
    private bool lastFeatherEdges;

    public void Draw(Graphics graphics, RectangleF stage, RectangleF rail, Rgb[] frame, float opacity = 1, bool featherEdges = false)
    {
        if (!ReferenceEquals(lastFrame, frame) || stage != lastStage || rail != lastRail || featherEdges != lastFeatherEdges)
        {
            Fill(stage, rail, frame, featherEdges);
            lastFeatherEdges = featherEdges;
            lastFrame = frame;
            lastStage = stage;
            lastRail = rail;
        }

        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        if (opacity >= 1)
        {
            graphics.DrawImage(texture, stage);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
        graphics.DrawImage(texture, Rectangle.Round(stage), 0, 0, Width, Height, GraphicsUnit.Pixel, attributes);
    }

    private void Fill(RectangleF stage, RectangleF rail, Rgb[] frame, bool featherEdges)
    {
        var palette = frame.Select(pixel => pixel.PreviewColor).ToArray();
        var softened = new Color[palette.Length];
        SmoothStrip(palette, softened, 0, Sk0127.Rows);
        SmoothStrip(palette, softened, Sk0127.Rows, Sk0127.Columns);
        SmoothStrip(palette, softened, Sk0127.Rows + Sk0127.Columns, Sk0127.Rows);
        palette = softened;
        var near = 2 * Math.Pow(stage.Width * .026f, 2);
        var far = 2 * Math.Pow(stage.Width * .094f, 2);
        var left = new Color[Height];
        var right = new Color[Height];
        var top = new Color[Width];
        for (var x = 0; x < Width; x++)
        {
            var u = Math.Clamp((stage.Left + stage.Width * x / (Width - 1) - rail.Left) / rail.Width, 0f, 1f);
            top[x] = Sample(palette, Sk0127.Rows + u * (Sk0127.Columns - 1));
        }

        for (var y = 0; y < Height; y++)
        {
            var py = stage.Top + stage.Height * y / (Height - 1);
            var v = Math.Clamp((py - rail.Top) / rail.Height, 0f, 1f);
            left[y] = Sample(palette, (1 - v) * (Sk0127.Rows - 1));
            right[y] = Sample(palette, Sk0127.Rows + Sk0127.Columns + v * (Sk0127.Rows - 1));
            var ySide = py - Math.Clamp(py, rail.Top, rail.Bottom);
            var yTop = py - rail.Top;
            for (var x = 0; x < Width; x++)
            {
                var px = stage.Left + stage.Width * x / (Width - 1);
                var xLeft = px - rail.Left;
                var xRight = px - rail.Right;
                var xTop = px - Math.Clamp(px, rail.Left, rail.Right);
                var l = Falloff(xLeft * xLeft + ySide * ySide, near, far);
                var t = Falloff(xTop * xTop + yTop * yTop, near, far);
                var r = Falloff(xRight * xRight + ySide * ySide, near, far);
                var pixel = Pixel(left[y], top[x], right[y], l, t, r);
                if (featherEdges)
                {
                    var edgeX = Math.Min(x, Width - 1 - x) / (Width * .13f);
                    var edgeY = Math.Min(y, Height - 1 - y) / (Height * .13f);
                    var edge = Math.Min(1f, Math.Min(edgeX, edgeY));
                    var alpha = (int)((int)((uint)pixel >> 24) * edge * edge * (3 - 2 * edge));
                    pixel = (pixel & 0x00ffffff) | (alpha << 24);
                }
                buffer[y * Width + x] = pixel;
            }
        }

        var bits = texture.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, texture.PixelFormat);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(buffer, y * Width, bits.Scan0 + y * bits.Stride, Width);
        }
        finally
        {
            texture.UnlockBits(bits);
        }
    }

    private static void SmoothStrip(Color[] input, Color[] output, int start, int length)
    {
        ReadOnlySpan<int> weights = [1, 4, 6, 4, 1];
        for (var i = start; i < start + length; i++)
        {
            var red = 0;
            var green = 0;
            var blue = 0;
            for (var tap = 0; tap < weights.Length; tap++)
            {
                var color = input[Math.Clamp(i + tap - 2, start, start + length - 1)];
                red += color.R * weights[tap];
                green += color.G * weights[tap];
                blue += color.B * weights[tap];
            }
            output[i] = Color.FromArgb(red / 16, green / 16, blue / 16);
        }
    }

    private static Color Sample(Color[] palette, float index)
    {
        var first = (int)index;
        var next = Math.Min(first + 1, palette.Length - 1);
        var fraction = index - first;
        int Mix(byte a, byte b) => (int)Math.Round(a + (b - a) * fraction);
        return Color.FromArgb(Mix(palette[first].R, palette[next].R), Mix(palette[first].G, palette[next].G),
            Mix(palette[first].B, palette[next].B));
    }

    private static double Falloff(float distanceSquared, double near, double far) =>
        1.45 * Math.Exp(-distanceSquared / near) + 1.75 * Math.Exp(-distanceSquared / far);

    private static int Pixel(Color left, Color top, Color right, double l, double t, double r)
    {
        var red = left.R * l + top.R * t + right.R * r;
        var green = left.G * l + top.G * t + right.G * r;
        var blue = left.B * l + top.B * t + right.B * r;
        var peak = Math.Max(red, Math.Max(green, blue));
        if (peak < 1)
            return 0;

        var alpha = (int)(217 * (1 - Math.Exp(-Math.Pow(peak / 345, 1.85))));
        var exposure = 255 / peak;
        return (alpha << 24) | ((int)Math.Min(255, red * exposure) << 16) |
            ((int)Math.Min(255, green * exposure) << 8) | (int)Math.Min(255, blue * exposure);
    }

    public void Dispose() => texture.Dispose();
}

