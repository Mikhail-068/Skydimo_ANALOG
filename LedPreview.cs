using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

namespace Kontur;

internal sealed class LedPreview : Control
{
    private readonly Font wallLabel = new("Segoe UI", 10);
    private readonly Font heading = new("Bahnschrift", 15);
    private Rgb[] pixels = new Rgb[Sk0127.Count];

    private readonly System.Windows.Forms.Timer animation = new() { Interval = 145 };
    private readonly AmbientWall ambient = new();
    private readonly Bitmap spill = new(Sk0127.Columns, Sk0127.Rows);
    private readonly Font codeFont = new("Consolas", 10);
    private int tick;
    private static readonly string[] Code = [
        "namespace Kontur.Rendering;", "",
        "var frame = await capture.NextAsync();",
        "var edges = frame.SampleEdges();", "",
        "foreach (var zone in edges)",
        "{",
        "    var light = zone.Color * brightness;",
        "    preview.Blend(zone.Position, light);",
        "}", "",
        "await controller.WriteAsync(edges);"
    ];

    public LedPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WindowStyle.Background;
        AccessibleName = "Экран предпросмотра";
        Cursor = Cursors.Default;
        Margin = Padding.Empty;
        animation.Tick += (_, _) => { if (Visible && FindForm()?.WindowState != FormWindowState.Minimized) { tick++; Invalidate(); } };
        animation.Start();
    }

    public void SetFrame(Rgb[] value)
    {
        if (value.AsSpan().SequenceEqual(pixels))
            return;
        pixels = value;
        for (var y = 0; y < spill.Height; y++)
        for (var x = 0; x < spill.Width; x++)
        {
            var left = pixels[Sk0127.Rows - 1 - y].PreviewColor;
            var top = pixels[Sk0127.Rows + x].PreviewColor;
            var right = pixels[Sk0127.Rows + Sk0127.Columns + y].PreviewColor;
            var l = Math.Exp(-x / 5d);
            var t = Math.Exp(-y / 4d);
            var r = Math.Exp(-(spill.Width - 1 - x) / 5d);
            var weight = Math.Max(1, l + t + r);
            int Mix(int a, int b, int c) => (int)((a * l + b * t + c * r) / weight);
            spill.SetPixel(x, y, Color.FromArgb(150, Mix(left.R, top.R, right.R),
                Mix(left.G, top.G, right.G), Mix(left.B, top.B, right.B)));
        }
        Invalidate();
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            var inset = 24 * DeviceDpi / 96;
            var bottom = 13 * DeviceDpi / 96;
            return new Rectangle(inset, 0, Math.Max(0, Width - 2 * inset), Math.Max(0, Height - bottom));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        WindowStyle.Satin(g, new RectangleF(.5f, .5f, Width - 1, Height - 1), 12 * scale, Color.FromArgb(31, 41, 53), WindowStyle.Panel, WindowStyle.Border);

        var inset = (int)(24 * scale);
        DrawText(g, "Предпросмотр", heading, new Rectangle(inset, (int)(19 * scale), Width - 2 * inset, (int)(33 * scale)),
            WindowStyle.Text, TextFormatFlags.Left);
        DrawText(g, "65 светодиодов", wallLabel, new Rectangle(inset, (int)(19 * scale), Width - 2 * inset, (int)(33 * scale)),
            WindowStyle.Muted, TextFormatFlags.Right);

        var stage = RectangleF.FromLTRB(inset, 65 * scale, Width - inset, Height - 116 * scale);
        if (stage.Width < 80 * scale || stage.Height < 80 * scale)
            return;
        DrawStage(g, stage, scale);
        DrawCounts(g, inset, scale);
    }

    private void DrawStage(Graphics g, RectangleF stage, float scale)
    {
        WindowStyle.Satin(g, stage, 8 * scale, Color.FromArgb(17, 32, 48), Color.FromArgb(8, 16, 32),
            Color.FromArgb(56, 78, 104));
        using var wallClip = WindowStyle.RoundedPath(stage, 8 * scale);
        var canvas = g.Save();
        g.SetClip(wallClip);

        var width = Math.Min(stage.Width * .72f, stage.Height * 1.23f);
        var height = width * 9 / 16;
        var monitor = new RectangleF(stage.Left + (stage.Width - width) / 2,
            stage.Top + (stage.Height - height) / 2 - 6 * scale, width, height);
        var rail = RectangleF.FromLTRB(monitor.Left - 12 * scale, monitor.Top - 12 * scale,
            monitor.Right + 12 * scale, monitor.Bottom);
        ambient.Draw(g, stage, rail, pixels);
        DrawLight(g, LedPositions(monitor, 12 * scale), scale);
        DrawMonitor(g, monitor, scale);
        g.Restore(canvas);
    }

    private void DrawLight(Graphics g, PointF[] positions, float scale)
    {
        var colors = pixels.Select(LightColor).ToArray();
        using var track = new Pen(Color.FromArgb(43, 51, 55), 3.5f * scale)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        g.DrawLines(track, positions);
        if (colors.All(color => color.ToArgb() == colors[0].ToArgb()))
        {
            if (colors[0].ToArgb() != Color.Black.ToArgb())
            {
                PaintUniformLight(g, positions, colors[0], 7 * scale, 44);
                PaintUniformLight(g, positions, colors[0], 2.5f * scale, 238);
            }
            return;
        }
        DrawLightPass(g, positions, colors, 8 * scale, 52);
        DrawLightPass(g, positions, colors, 2.5f * scale, 232);
    }

    private static void PaintUniformLight(Graphics g, PointF[] positions, Color color, float width, int opacity)
    {
        using var pen = new Pen(Color.FromArgb(opacity, color), width)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        g.DrawLines(pen, positions);
    }

    private static void DrawLightPass(Graphics g, PointF[] positions, Color[] colors, float width, int opacity)
    {
        using var pen = new Pen(Color.Transparent, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 1; i < positions.Length; i++)
        {
            var first = colors[i - 1];
            var next = colors[i];
            if (first.ToArgb() == Color.Black.ToArgb() && next.ToArgb() == Color.Black.ToArgb())
                continue;

            using var bridge = new LinearGradientBrush(positions[i - 1], positions[i],
                first.ToArgb() == Color.Black.ToArgb() ? Color.FromArgb(0, next) : Color.FromArgb(opacity, first),
                next.ToArgb() == Color.Black.ToArgb() ? Color.FromArgb(0, first) : Color.FromArgb(opacity, next));
            pen.Brush = bridge;
            g.DrawLine(pen, positions[i - 1], positions[i]);
        }
    }

    private static Color LightColor(Rgb rgb)
    {
        static int Highlight(int channel) => Math.Min(255, (int)(channel * 1.93));
        var preview = rgb.PreviewColor;
        return Color.FromArgb(Highlight(preview.R), Highlight(preview.G), Highlight(preview.B));
    }

    private void DrawMonitor(Graphics g, RectangleF monitor, float scale)
    {
        using var pedestal = new Pen(Color.FromArgb(116, 125, 132), 1.35f * scale);
        var center = monitor.Left + monitor.Width / 2;
        var foot = monitor.Bottom + 22 * scale;
        g.DrawLine(pedestal, center, monitor.Bottom, center, foot);
        g.DrawLine(pedestal, center - 35 * scale, foot, center + 35 * scale, foot);
        WindowStyle.Satin(g, monitor, 10 * scale, Color.FromArgb(176, 196, 218), Color.FromArgb(73, 97, 129), Color.FromArgb(122, 130, 136));

        var surface = RectangleF.Inflate(monitor, -3 * scale, -3 * scale);
        WindowStyle.Satin(g, surface, 7 * scale, Color.FromArgb(35, 57, 82), Color.FromArgb(22, 34, 53), Color.FromArgb(15, 18, 20));
        using var face = WindowStyle.RoundedPath(surface, 7 * scale);
        var clipped = g.Save();
        g.SetClip(face);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        if (pixels.Any(pixel => pixel != default))
            g.DrawImage(spill, surface);
        var margin = 16 * scale;
        var row = 18 * scale;
        var rows = Math.Min(Code.Length, Math.Max(1, (int)((surface.Height - 45 * scale) / row)));
        using var toolbar = new SolidBrush(Color.FromArgb(125, 32, 50, 74));
        g.FillRectangle(toolbar, surface.X, surface.Y, surface.Width, 27 * scale);
        TextRenderer.DrawText(g, "lighting.cs", codeFont,
            Rectangle.Round(new RectangleF(surface.X + margin, surface.Y, surface.Width - margin, 27 * scale)), WindowStyle.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var step = tick % (Code.Length + 5);
        var first = Math.Max(0, Math.Min(step, Code.Length - 1) - rows + 1);
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var i = first + rowIndex;
            var text = Code[i];
            if (i > step)
                break;
            if (i == step && tick % 2 == 0)
                text += " ▏";
            var area = new RectangleF(surface.X + margin, surface.Y + 33 * scale + rowIndex * row, surface.Width - 2 * margin, row);
            TextRenderer.DrawText(g, text, codeFont, Rectangle.Round(area),
                i == step ? WindowStyle.Accent : i % 3 == 0 ? Color.FromArgb(182, 214, 254) : WindowStyle.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }
        g.Restore(clipped);
    }


    private void DrawCounts(Graphics g, int inset, float scale)
    {
        var bounds = RectangleF.FromLTRB(inset, Height - 111 * scale, Width - inset, Height - 76 * scale);
        var names = new[] { "Слева 17", "Сверху 31", "Справа 17" };
        var width = bounds.Width / names.Length;
        using var pen = new Pen(WindowStyle.Border);
        for (var i = 0; i < names.Length; i++)
        {
            var cell = new RectangleF(bounds.Left + i * width, bounds.Top, width, bounds.Height);
            if (i > 0)
                g.DrawLine(pen, cell.Left, cell.Top + 8 * scale, cell.Left, cell.Bottom - 8 * scale);
            DrawText(g, names[i], wallLabel, Rectangle.Round(cell), WindowStyle.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    private static PointF[] LedPositions(RectangleF screen, float offset)
    {
        var result = new PointF[Sk0127.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var cell = Sk0127.Cells[i];
            if (i >= Sk0127.Rows && i < Sk0127.Rows + Sk0127.Columns)
                result[i] = new PointF(screen.Left + screen.Width * cell.X / (Sk0127.Columns - 1), screen.Top - offset);
            else
                result[i] = new PointF(i < Sk0127.Rows ? screen.Left - offset : screen.Right + offset,
                    screen.Top + screen.Height * cell.Y / (Sk0127.Rows - 1));
        }
        return result;
    }

    private static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags alignment) =>
        TextRenderer.DrawText(g, text, font, bounds, color, alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose();
            ambient.Dispose();
            spill.Dispose();
            codeFont.Dispose();
            heading.Dispose();
            wallLabel.Dispose();
        }
        base.Dispose(disposing);
    }
}
