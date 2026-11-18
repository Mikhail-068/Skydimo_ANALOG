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
        DrawMonitor(g, monitor, scale);
        g.Restore(canvas);
    }

    private void DrawMonitor(Graphics g, RectangleF monitor, float scale)
    {
        DrawStand(g, monitor, scale);

        var depth = monitor;
        depth.Offset(2 * scale, 3 * scale);
        WindowStyle.Satin(g, depth, 6 * scale, Color.FromArgb(27, 32, 38),
            Color.FromArgb(6, 8, 11), Color.FromArgb(10, 13, 16));
        WindowStyle.Satin(g, monitor, 5 * scale, Color.FromArgb(59, 66, 74),
            Color.FromArgb(29, 35, 42), Color.FromArgb(106, 119, 133));

        var surface = RectangleF.FromLTRB(monitor.Left + 4 * scale, monitor.Top + 4 * scale,
            monitor.Right - 4 * scale, monitor.Bottom - 11 * scale);
        WindowStyle.Satin(g, surface, 2 * scale, Color.FromArgb(24, 38, 55),
            Color.FromArgb(10, 16, 25), Color.FromArgb(5, 8, 12));
        using var diode = new SolidBrush(Color.FromArgb(167, 204, 225));
        g.FillEllipse(diode, monitor.Right - 17 * scale, monitor.Bottom - 6 * scale, 4 * scale, 2 * scale);

        using var face = WindowStyle.RoundedPath(surface, 2 * scale);
        var clipped = g.Save();
        g.SetClip(face);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        if (pixels.Any(pixel => pixel != default))
            g.DrawImage(spill, surface);
        using var reflection = new LinearGradientBrush(surface, Color.FromArgb(0, 193, 215, 235),
            Color.FromArgb(29, 193, 215, 235), 315f);
        g.FillPolygon(reflection,
        [
            new PointF(surface.Left + surface.Width * .44f, surface.Top),
            new PointF(surface.Right, surface.Top),
            new PointF(surface.Right, surface.Bottom),
            new PointF(surface.Left + surface.Width * .72f, surface.Bottom)
        ]);

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


    private static void DrawStand(Graphics g, RectangleF monitor, float scale)
    {
        var center = monitor.Left + monitor.Width / 2;
        var baseY = monitor.Bottom + 30 * scale;
        var shadow = new RectangleF(center - 92 * scale, baseY - 7 * scale, 184 * scale, 31 * scale);
        using var shadowPath = new GraphicsPath();
        shadowPath.AddEllipse(shadow);
        using var shade = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(205, 0, 0, 0),
            SurroundColors = [Color.Transparent]
        };
        g.FillPath(shade, shadowPath);

        var neck = new RectangleF(center - 9 * scale, monitor.Bottom - 4 * scale, 18 * scale, 39 * scale);
        using var metal = new LinearGradientBrush(neck, Color.FromArgb(79, 89, 101),
            Color.FromArgb(23, 29, 36), LinearGradientMode.Horizontal);
        g.FillRectangle(metal, neck);
        using var lip = new Pen(Color.FromArgb(116, 128, 142), scale);
        var baseTop = new RectangleF(center - 61 * scale, baseY, 122 * scale, 15 * scale);
        var baseEdge = baseTop;
        baseEdge.Offset(0, 4 * scale);
        using var edge = new SolidBrush(Color.FromArgb(11, 15, 20));
        g.FillEllipse(edge, baseEdge);
        using var top = new LinearGradientBrush(baseTop, Color.FromArgb(79, 89, 102),
            Color.FromArgb(27, 33, 41), LinearGradientMode.Vertical);
        g.FillEllipse(top, baseTop);
        g.DrawArc(lip, baseTop, 14, 152);
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
