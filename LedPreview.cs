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
    private readonly System.Windows.Forms.Timer directionTimer = new() { Interval = 38 };
    private int directionStep;
    private bool clearedCanvas;
    private readonly StudioButton clearRoute = new("Очистить", "Очистить маршрут") { Visible = false };
    private readonly StudioButton startDrawing = new("Начать рисовать", "Начать рисовать")
    {
        Primary = true,
        Visible = false
    };
    private readonly System.Windows.Forms.Timer flip = new() { Interval = 15 };
    private readonly System.Diagnostics.Stopwatch flipClock = new();
    private readonly StudioButton installation = new("Настройка положения ленты", "Настройка положения ленты");
    private float rotation;
    private float flipFrom;
    private bool showRear;
    private RectangleF rearSurface;
    private RectangleF drawingCanvas;
    private bool processingRoute;
    private readonly ProgressBar routeProgress = new()
    {
        Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28,
        Visible = false, AccessibleName = "Выравнивание маршрута"
    };
    private List<PointF>? drawing;
    private PointF[]? stripPath;
    private int[]? wallIndices;
    private Rgb[] routePixels = new Rgb[Sk0127.Count];
    private bool reverseStrip;

    public event Action? StripPathChanged;

    public PointF[]? StripPath
    {
        get => stripPath;
        set
        {
            stripPath = value;
            if (value is not null)
                clearedCanvas = false;
            directionStep = 0;
            wallIndices = value is null ? null : Sk0127.Cells.Select(cell =>
                Enumerable.Range(0, value.Length).MinBy(index =>
                {
                    var x = (1 - value[index].X) * (Sk0127.Columns - 1) - cell.X;
                    var y = value[index].Y * (Sk0127.Rows - 1) - cell.Y;
                    return x * x + y * y;
                })).ToArray();
            SetFrame(routePixels);
            Invalidate();
        }
    }

    public bool ReverseStrip
    {
        get => reverseStrip;
        set
        {
            reverseStrip = value;
            Invalidate();
        }
    }
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
        directionTimer.Tick += (_, _) =>
        {
            if (!Visible || FindForm()?.WindowState == FormWindowState.Minimized || flip.Enabled || processingRoute || drawing is not null)
                return;
            directionStep = (directionStep + 1) % (Sk0127.Count + 12);
            Invalidate();
        };
        installation.Click += (_, _) =>
        {
            showRear = !showRear;
            startDrawing.Visible = false;
            clearRoute.Visible = false;
            directionStep = 0;
            directionTimer.Enabled = showRear;
            installation.Primary = showRear;
            installation.Text = showRear ? "Вид спереди" : "Настройка положения ленты";
            flipFrom = rotation;
            flipClock.Restart();
            flip.Start();
            installation.Invalidate();
        };
        Controls.Add(installation);
        Controls.Add(routeProgress);
        startDrawing.Click += (_, _) =>
        {
            startDrawing.Visible = false;
            clearRoute.Visible = true;
            directionStep = 0;
            Invalidate();
        };
        Controls.Add(startDrawing);
        clearRoute.Click += (_, _) => ClearRoute();
        Controls.Add(clearRoute);
        flip.Tick += (_, _) =>
        {
            var t = Math.Min(1f, (float)flipClock.Elapsed.TotalMilliseconds / 360);
            rotation = flipFrom + ((showRear ? 1f : 0f) - flipFrom) * t * t * (3 - 2 * t);
            if (t >= 1)
            {
                flip.Stop();
                startDrawing.Visible = showRear;
            }
            Invalidate();
        };
    }

    public void SetFrame(Rgb[] value)
    {
        routePixels = value;
        if (wallIndices is not null)
            value = wallIndices.Select(index => routePixels[index]).ToArray();
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

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var scale = DeviceDpi / 96f;
        var width = (int)(153 * scale);
        var installationWidth = (int)(193 * scale);
        installation.SetBounds((Width - installationWidth) / 2 + (int)(16 * scale), (int)(21 * scale), installationWidth, (int)(29 * scale));
        var stageCenter = (Height - 51 * scale) / 2;
        startDrawing.SetBounds((Width - width) / 2, (int)(stageCenter + 65 * scale), width, (int)(29 * scale));
        var clearWidth = (int)(108 * scale);
        clearRoute.SetBounds((Width - clearWidth) / 2, (int)(74 * scale), clearWidth, (int)(29 * scale));
        routeProgress.SetBounds((Width - width) / 2, Height - (int)(85 * scale), width, (int)(5 * scale));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!showRear || flip.Enabled || startDrawing.Visible || processingRoute || !drawingCanvas.Contains(e.Location))
            return;

        if (e.Button == MouseButtons.Right)
        {
            ClearRoute();
        }
        else if (e.Button == MouseButtons.Left)
        {
            drawing = [Normalize(e.Location)];
            Capture = true;
            Invalidate();
        }
    }

    private void ClearRoute()
    {
        if (processingRoute)
            return;
        clearedCanvas = true;
        StripPath = null;
        StripPathChanged?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drawing is null)
            return;
        AddPoint(e.Location);
    }

    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || drawing is null)
            return;

        AddPoint(e.Location);
        var stroke = drawing;
        drawing = null;
        Capture = false;
        var size = rearSurface.Size;
        var scale = DeviceDpi / 96f;
        processingRoute = true;
        installation.Enabled = false;
        clearRoute.Enabled = false;
        routeProgress.Visible = true;
        Invalidate();
        try
        {
            var route = await Task.Run(() => BuildRoute(stroke, size, scale));
            if (!IsDisposed && route is not null)
            {
                StripPath = route;
                StripPathChanged?.Invoke();
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                processingRoute = false;
                installation.Enabled = true;
                clearRoute.Enabled = true;
                routeProgress.Visible = false;
                Invalidate();
            }
        }
    }

    private static PointF[]? BuildRoute(List<PointF> stroke, SizeF surface, float scale)
    {
        var points = Straighten(stroke, surface, scale);
        var lengths = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
        {
            var dx = (points[i].X - points[i - 1].X) * surface.Width;
            var dy = (points[i].Y - points[i - 1].Y) * surface.Height;
            lengths[i] = lengths[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        var total = lengths[^1];
        if (points.Count > 1 && total >= 12 * scale)
        {
            var sampled = new PointF[Sk0127.Count];
            sampled[0] = points[0];
            var previousIndex = 0;
            for (var segment = 1; segment < points.Count; segment++)
            {
                var idealIndex = (int)Math.Round(lengths[segment] / total * (sampled.Length - 1));
                var lastIndex = sampled.Length - points.Count + segment;
                var index = Math.Clamp(idealIndex, previousIndex + 1, lastIndex);
                var a = points[segment - 1];
                var b = points[segment];
                for (var i = previousIndex + 1; i <= index; i++)
                {
                    var t = (float)(i - previousIndex) / (index - previousIndex);
                    sampled[i] = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                }
                previousIndex = index;
            }
            return sampled;
        }
        return null;
    }

    private static List<PointF> Straighten(List<PointF> stroke, SizeF surface, float scale)
    {
        var width = surface.Width;
        var height = surface.Height;
        var points = stroke.Select(point => new PointF(point.X * width, point.Y * height)).ToArray();
        var keep = new bool[points.Length];
        keep[0] = keep[^1] = true;
        var pending = new Stack<(int First, int Last)>();
        pending.Push((0, points.Length - 1));
        var tolerance = 14 * scale;

        var vertexCount = Math.Min(2, points.Length);
        while (vertexCount < Sk0127.Count && pending.TryPop(out var segment))
        {
            var first = points[segment.First];
            var last = points[segment.Last];
            var dx = last.X - first.X;
            var dy = last.Y - first.Y;
            var lengthSquared = dx * dx + dy * dy;
            var threshold = tolerance * tolerance;
            var split = -1;
            for (var i = segment.First + 1; i < segment.Last; i++)
            {
                var x = points[i].X - first.X;
                var y = points[i].Y - first.Y;
                var t = lengthSquared == 0 ? 0 : Math.Clamp((x * dx + y * dy) / lengthSquared, 0, 1);
                x -= dx * t;
                y -= dy * t;
                var distance = x * x + y * y;
                if (distance > threshold)
                {
                    threshold = distance;
                    split = i;
                }
            }

            if (split >= 0)
            {
                keep[split] = true;
                vertexCount++;
                pending.Push((segment.First, split));
                pending.Push((split, segment.Last));
            }
        }

        var vertices = points.Where((_, index) => keep[index]).ToList();
        for (var i = 1; i + 2 < vertices.Count; i++)
        {
            var before = NearAxis(vertices[i - 1], vertices[i]);
            var after = NearAxis(vertices[i + 1], vertices[i + 2]);
            if (before == 0 || after == 0 || before == after || NearAxis(vertices[i], vertices[i + 1]) != 0)
                continue;

            var bevel = Length(vertices[i], vertices[i + 1]);
            var adjacent = Math.Min(Length(vertices[i - 1], vertices[i]), Length(vertices[i + 1], vertices[i + 2]));
            if (bevel > adjacent * .4f || bevel > Math.Min(width, height) * .25f)
                continue;

            var a = vertices[i];
            var b = vertices[i + 1];
            vertices[i] = before == 1 ? new PointF(b.X, a.Y) : new PointF(a.X, b.Y);
            vertices.RemoveAt(i + 1);
        }

        var corners = vertices.ToArray();
        var aligned = (PointF[])corners.Clone();
        for (var i = 0; i < corners.Length - 1;)
        {
            var axis = Axis(corners[i], corners[i + 1]);
            var end = i + 1;
            while (end < corners.Length - 1 && Axis(corners[end], corners[end + 1]) == axis)
                end++;
            var x = (corners[i].X + corners[end].X) / 2;
            var y = (corners[i].Y + corners[end].Y) / 2;
            for (var j = i; j <= end; j++)
                aligned[j] = axis == 1 ? new PointF(aligned[j].X, y) : new PointF(x, aligned[j].Y);
            i = end;
        }

        var normalized = new List<PointF>(aligned.Length);
        foreach (var point in aligned)
        {
            var next = new PointF(point.X / width, point.Y / height);
            if (normalized.Count == 0 || normalized[^1] != next)
                normalized.Add(next);
        }
        return normalized;

        static float Length(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        static int Axis(PointF first, PointF last) =>
            Math.Abs(last.X - first.X) >= Math.Abs(last.Y - first.Y) ? 1 : 2;

        static int NearAxis(PointF first, PointF last)
        {
            var x = Math.Abs(last.X - first.X);
            var y = Math.Abs(last.Y - first.Y);
            if (y <= x * .28f)
                return 1;
            return x <= y * .28f ? 2 : 0;
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (drawing is not null && !Capture)
        {
            drawing = null;
            Invalidate();
        }
    }

    private PointF Normalize(Point point) => new(
        Math.Clamp((point.X - rearSurface.Left) / rearSurface.Width, 0, 1),
        Math.Clamp((point.Y - rearSurface.Top) / rearSurface.Height, 0, 1));

    private void AddPoint(Point location)
    {
        var point = Normalize(location);
        if (drawing is not null && drawing[^1] != point)
        {
            drawing.Add(point);
            Invalidate();
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
        drawingCanvas = stage;
        DrawStage(g, stage, scale);
        if (startDrawing.Visible)
            DrawDrawingHint(g, stage, scale);
        if (showRear)
            DrawText(g, processingRoute ? "Выравниваю маршрут…" : flip.Enabled ? "Разворачиваю монитор…" :
                startDrawing.Visible ? "" : "Нарисуй ленту мышью", wallLabel,
                new Rectangle(inset, Height - (int)(111 * scale), Width - 2 * inset, (int)(35 * scale)),
                WindowStyle.Muted, TextFormatFlags.HorizontalCenter);
        else
            DrawCounts(g, inset, scale);
    }

    private void DrawDrawingHint(Graphics g, RectangleF stage, float scale)
    {
        using var dim = new SolidBrush(Color.FromArgb(158, WindowStyle.Background));
        using var curtain = WindowStyle.RoundedPath(stage, 8 * scale);
        g.FillPath(dim, curtain);

        var width = Math.Min(stage.Width - 12 * scale, 390 * scale);
        var panel = new RectangleF(stage.Left + (stage.Width - width) / 2,
            stage.Top + stage.Height / 2 - 115 * scale, width, 225 * scale);
        WindowStyle.Satin(g, panel, 9 * scale, Color.FromArgb(37, 47, 55),
            WindowStyle.Panel, WindowStyle.Border);
        void TextLine(string text, Font font, float top, Color color)
        {
            var line = new RectangleF(panel.Left + 8 * scale, panel.Top + top * scale, width - 16 * scale, 24 * scale);
            DrawText(g, text, font, Rectangle.Round(line), color, TextFormatFlags.HorizontalCenter);
        }

        TextLine("Как провести ленту", heading, 10, WindowStyle.Text);
        TextLine("Зажми левую кнопку в точке подключения.", wallLabel, 37, WindowStyle.Text);
        TextLine("Проведи вдоль ленты и отпусти в конце.", wallLabel, 58, WindowStyle.Text);

        var left = panel.Left + width * .29f;
        var right = panel.Right - width * .29f;
        var top = panel.Top + 96 * scale;
        var bottom = panel.Top + 150 * scale;
        PointF[] route = [new(right, bottom), new(right, top), new(left, top), new(left, bottom)];
        using var outline = new Pen(WindowStyle.Border, 2 * scale) { DashStyle = DashStyle.Dash };
        g.DrawLines(outline, route);
        using var ink = new Pen(WindowStyle.Active, 3 * scale) { EndCap = LineCap.Round, StartCap = LineCap.Round };
        var progress = Math.Clamp((directionStep - 6) / 54f, 0, 1);
        var distance = progress * (right - left + 2 * (bottom - top));
        var cursor = route[0];
        for (var i = 1; i < route.Length; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            var length = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
            var t = Math.Clamp(distance / length, 0, 1);
            cursor = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            if (t > 0)
                g.DrawLine(ink, a, cursor);
            distance -= length;
            if (distance <= 0)
                break;
        }
        using var pointer = new SolidBrush(WindowStyle.Text);
        g.FillPolygon(pointer,
        [
            cursor,
            new PointF(cursor.X + 4 * scale, cursor.Y + 14 * scale),
            new PointF(cursor.X + 7 * scale, cursor.Y + 9 * scale),
            new PointF(cursor.X + 13 * scale, cursor.Y + 9 * scale)
        ]);
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
        var apparentWidth = monitor.Width * Math.Max(.045f, (float)Math.Abs(Math.Cos(rotation * Math.PI)));
        monitor.Inflate((apparentWidth - monitor.Width) / 2, 0);
        if (rotation >= .5f)
            DrawRear(g, monitor, scale);
        else
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

        if (flip.Enabled)
            return;

        var surface = RectangleF.FromLTRB(monitor.Left + 4 * scale, monitor.Top + 4 * scale,
            monitor.Right - 4 * scale, monitor.Bottom - 11 * scale);
        WindowStyle.Satin(g, surface, 2 * scale, Color.FromArgb(24, 38, 55),
            Color.FromArgb(10, 16, 25), Color.FromArgb(5, 8, 12));
        using var rib = new Pen(Color.FromArgb(55, 62, 69), .65f * scale);
        for (var x = monitor.Left + 5 * scale; x < monitor.Right - 5 * scale; x += 2 * scale)
            g.DrawLine(rib, x, monitor.Bottom - 9 * scale, x, monitor.Bottom - 3 * scale);
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


    private void DrawRear(Graphics g, RectangleF monitor, float scale)
    {
        DrawStand(g, monitor, scale);
        WindowStyle.Satin(g, monitor, 7 * scale, Color.FromArgb(47, 54, 60),
            Color.FromArgb(15, 19, 23), Color.FromArgb(88, 97, 103));
        if (flip.Enabled)
            return;

        var mount = new RectangleF(monitor.Left + monitor.Width * .42f, monitor.Top + monitor.Height * .38f,
            monitor.Width * .16f, monitor.Height * .30f);
        WindowStyle.Satin(g, mount, 5 * scale, Color.FromArgb(45, 50, 54),
            Color.FromArgb(22, 26, 29), Color.FromArgb(65, 72, 77));
        var inset = 13 * scale;
        var left = monitor.Left + inset;
        var right = monitor.Right - inset;
        var top = monitor.Top + inset;
        var bottom = monitor.Bottom - 28 * scale;
        rearSurface = RectangleF.FromLTRB(left, top, right, bottom);
        var rearLeft = new PointF(left, bottom);
        var rearRight = new PointF(right, bottom);
        PointF Project(PointF point) => new(left + point.X * (right - left), top + point.Y * (bottom - top));
        using var backing = new Pen(Color.FromArgb(85, 91, 93), 6 * scale)
        {
            LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        if (drawing is { Count: > 1 } draft)
        {
            backing.Color = WindowStyle.Active;
            g.DrawLines(backing, draft.Select(Project).ToArray());
            return;
        }

        if (clearedCanvas && stripPath is null)
            return;

        var custom = stripPath?.Select(Project).ToArray();
        var start = custom is not null ? custom[0] : reverseStrip ? rearLeft : rearRight;
        var outward = start.X < monitor.Left + monitor.Width / 2 ? -1 : 1;
        Color[] insulation = [Color.FromArgb(183, 85, 79), Color.FromArgb(171, 181, 154), Color.FromArgb(103, 126, 141)];
        using var wire = new Pen(Color.Empty, 1.15f * scale) { EndCap = LineCap.Round };
        for (var i = 0; i < insulation.Length; i++)
        {
            var x = start.X + (i - 1) * 2.6f * scale;
            wire.Color = insulation[i];
            g.DrawBezier(wire, x, start.Y, x, start.Y + 17 * scale,
                x + outward * 12 * scale, start.Y + 12 * scale,
                x + outward * (19 + i * 2) * scale, start.Y + (53 + i * 2) * scale);
        }
        g.DrawLines(backing, custom ?? [rearLeft, new(left, top), new(right, top), rearRight]);
        using var diode = new SolidBrush(Color.Empty);
        var size = 3 * scale;
        for (var i = 0; i < Sk0127.Count; i++)
        {
            var cell = Sk0127.Cells[i];
            var point = custom is not null ? custom[i] : new PointF(
                right - (right - left) * cell.X / (Sk0127.Columns - 1),
                top + (bottom - top) * cell.Y / (Sk0127.Rows - 1));
            var pixel = routePixels[i];
            diode.Color = pixel == default ? Color.FromArgb(216, 207, 169) : pixel.PreviewColor;
            g.FillRectangle(diode, point.X - size / 2, point.Y - size / 2, size, size);

            var orderedIndex = custom is null && reverseStrip ? Sk0127.Count - 1 - i : i;
            var trail = directionStep - orderedIndex;
            if (trail is >= 0 and < 6)
            {
                var radius = (6f - trail * .65f) * scale;
                diode.Color = Color.FromArgb(110 - trail * 15, WindowStyle.Active);
                g.FillEllipse(diode, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                diode.Color = Color.FromArgb(255 - trail * 28, 171, 255, 227);
                g.FillRectangle(diode, point.X - size / 2, point.Y - size / 2, size, size);
            }
        }

        if (custom is not null)
        {
            DrawEndpoint(custom[0], true, custom[0].X < monitor.Left + monitor.Width / 2);
            DrawEndpoint(custom[^1], false, custom[^1].X < monitor.Left + monitor.Width / 2);
        }
        else
        {
            DrawEndpoint(rearLeft, reverseStrip, true);
            DrawEndpoint(rearRight, !reverseStrip, false);
        }

        void DrawEndpoint(PointF point, bool start, bool onLeft)
        {
            var color = start ? WindowStyle.Active : WindowStyle.Silver;
            WindowStyle.Rounded(g, new RectangleF(point.X - 5 * scale, point.Y - 5 * scale, 10 * scale, 10 * scale),
                5 * scale, color);
            var x = onLeft ? point.X + 10 * scale : point.X - 80 * scale;
            DrawText(g, start ? "Начало" : "Конец", wallLabel,
                Rectangle.Round(new RectangleF(x, point.Y - 13 * scale, 70 * scale, 26 * scale)),
                color, onLeft ? TextFormatFlags.Left : TextFormatFlags.Right);
        }
    }

    private static void DrawStand(Graphics g, RectangleF monitor, float scale)
    {
        var center = monitor.Left + monitor.Width / 2;
        var baseY = monitor.Bottom + 34 * scale;
        var span = 105 * scale;
        var shadow = new RectangleF(center - span - 15 * scale, baseY - 4 * scale,
            2 * span + 30 * scale, 26 * scale);
        using var shadowPath = new GraphicsPath();
        shadowPath.AddEllipse(shadow);
        using var shade = new PathGradientBrush(shadowPath)
        {
            CenterColor = Color.FromArgb(205, 0, 0, 0),
            SurroundColors = [Color.Transparent]
        };
        g.FillPath(shade, shadowPath);

        var neck = new RectangleF(center - 13 * scale, monitor.Bottom - 4 * scale, 26 * scale, 38 * scale);
        using var metal = new LinearGradientBrush(neck, Color.FromArgb(65, 70, 79),
            Color.FromArgb(19, 23, 29), LinearGradientMode.Horizontal);
        g.FillRectangle(metal, neck);
        using var leg = new SolidBrush(Color.FromArgb(38, 43, 51));
        using var accent = new Pen(Color.FromArgb(179, 39, 53), 2 * scale);
        using var bevel = new Pen(Color.FromArgb(95, 101, 109), scale);
        foreach (var side in new[] { -1, 1 })
        {
            var joint = new PointF(center + side * 10 * scale, baseY - 7 * scale);
            var tip = new PointF(center + side * span, baseY + 13 * scale);
            g.FillPolygon(leg, [joint, tip, new(tip.X, tip.Y + 4 * scale),
                new(center + side * 8 * scale, baseY + 3 * scale)]);
            g.DrawLine(accent, joint, tip);
            g.DrawLine(bevel, new PointF(center + side * 8 * scale, baseY + 3 * scale),
                new PointF(tip.X, tip.Y + 4 * scale));
        }
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
            flip.Dispose();
            directionTimer.Dispose();
            ambient.Dispose();
            spill.Dispose();
            codeFont.Dispose();
            heading.Dispose();
            wallLabel.Dispose();
        }
        base.Dispose(disposing);
    }
}
