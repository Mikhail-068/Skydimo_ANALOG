using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kontur;

internal static class WindowStyle
{
    public static readonly Color Background = Color.FromArgb(8, 11, 16);
    public static readonly Color Panel = Color.FromArgb(22, 28, 36);
    public static readonly Color Field = Color.FromArgb(30, 37, 47);
    public static readonly Color Border = Color.FromArgb(58, 72, 90);
    public static readonly Color BorderSoft = Color.FromArgb(40, 52, 68);
    public static readonly Color GlowColor = Color.FromArgb(38, 58, 78);
    public static readonly Color TextDim = Color.FromArgb(150, 163, 178);
    public static readonly Color Text = Color.FromArgb(243, 245, 246);
    public static readonly Color Muted = Color.FromArgb(195, 205, 214);
    public static readonly Color Silver = Color.FromArgb(214, 223, 226);
    public static readonly Color Accent = Color.FromArgb(255, 198, 112);
    public static readonly Color Active = Color.FromArgb(122, 232, 208);
    public static readonly Color Status = Color.FromArgb(122, 232, 208);

    public static Label Label(string text, float size = 10, bool bold = false, Color? color = null, string font = "Segoe UI") =>
        new() { Text = text, Font = new Font(font, size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };

    public static void Rounded(Graphics g, RectangleF bounds, float radius, Color fill, Color? border = null)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = RoundedPath(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        if (border is Color color)
        {
            using var pen = new Pen(color);
            g.DrawPath(pen, shape);
        }
    }

    public static void Satin(Graphics g, RectangleF bounds, float radius, Color top, Color bottom, Color edge)
    {
        Satin(g, bounds, radius, top, bottom, edge, null);
    }

    public static void Satin(Graphics g, RectangleF bounds, float radius, Color top, Color bottom, Color edge, Color? glow = null)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (glow is Color color) Glow(g, bounds, radius, color);
        using var path = RoundedPath(bounds, radius);
        using var fill = new LinearGradientBrush(bounds, top, bottom, 90f);
        using var outline = new Pen(edge);
        g.FillPath(fill, path);
        g.DrawPath(outline, path);
    }

    public static void Glow(Graphics g, RectangleF bounds, float radius, Color color)
    {
        var size = 1.9f;
        var expanded = RectangleF.Inflate(bounds, radius * size, radius * size);
        using var path = RoundedPath(expanded, radius * size);
        using var fade = new LinearGradientBrush(expanded, color, Color.FromArgb(0, color.R, color.G, color.B), 90f);
        g.FillPath(fade, path);
    }

    public static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static readonly Lazy<Bitmap> DashboardGrain = new(CreateDashboardGrain);

    private static Bitmap CreateDashboardGrain()
    {
        const int size = 128;
        var random = new Random(2106);
        var grain = new Bitmap(size, size);
        var heights = new int[size, size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                heights[x, y] = random.Next(32);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var previous = heights[(x + size - 1) % size, (y + size - 1) % size];
                var relief = heights[x, y] - previous;
                var tone = relief > 0 ? 171 : 0;
                grain.SetPixel(x, y, Color.FromArgb(Math.Abs(relief) * 2, tone, tone, tone));
            }
        }
        return grain;
    }

    public static void DrawDashboardHeader(Graphics g, Size size, float scale)
    {
        var frame = new RectangleF(.5f, .5f, size.Width - 1, size.Height - 4 * scale);
        if (frame.Width <= 1 || frame.Height <= 1)
            return;

        Satin(g, frame, 7 * scale, Color.FromArgb(60, 66, 69),
            Color.FromArgb(22, 26, 29), Color.FromArgb(86, 93, 95));
        var face = RectangleF.Inflate(frame, -4 * scale, -4 * scale);
        DrawDashboardPlastic(g, face, scale);
    }

    public static void DrawDashboardPlastic(Graphics g, RectangleF face, float scale, int shadeOpacity = 0)
    {
        if (face.Width <= 2 || face.Height <= 2)
            return;

        Satin(g, face, 5 * scale, Color.FromArgb(45, 52, 55),
            Color.FromArgb(24, 30, 34), Color.FromArgb(15, 19, 21));
        using var plastic = RoundedPath(RectangleF.Inflate(face, -1, -1), 4 * scale);
        using var texture = new TextureBrush(DashboardGrain.Value);
        g.FillPath(texture, plastic);
        if (shadeOpacity > 0)
        {
            using var shade = new SolidBrush(Color.FromArgb(shadeOpacity, Color.Black));
            g.FillPath(shade, plastic);
        }
    }

    public static void Caption(IntPtr window)
    {
        const int immersiveDarkMode = 20;
        const int captionColor = 35;
        const int textColor = 36;
        int enabled = 1;
        int caption = ColorTranslator.ToWin32(Background);
        int captionText = ColorTranslator.ToWin32(Text);
        _ = DwmSetWindowAttribute(window, immersiveDarkMode, ref enabled, sizeof(int));
        _ = DwmSetWindowAttribute(window, captionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(window, textColor, ref captionText, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class Card : Panel
{
    public Card()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WindowStyle.Panel;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(WindowStyle.Background);
        var scale = DeviceDpi / 96f;
        var rect = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        WindowStyle.Glow(e.Graphics, rect, 15 * scale, Color.FromArgb(36, 54, 72));
        WindowStyle.Satin(e.Graphics, rect, 13 * scale,
            Color.FromArgb(40, 50, 61), WindowStyle.Panel, WindowStyle.Border);
        using var trim = new Pen(Color.FromArgb(212, 165, 106), 1.3f * scale);
        e.Graphics.DrawLine(trim, 15 * scale, 1, Math.Min(90 * scale, Width - 15 * scale), 1);
        using var corner = new Pen(Color.FromArgb(196, 226, 214), 1.3f * scale);
        e.Graphics.DrawLine(corner, Width - 1 - 15 * scale, 1, Width - 1 - 15 * scale, 1 + 34 * scale);
        using var baseLine = new Pen(Color.FromArgb(96, 108, 119), 1.2f * scale);
        e.Graphics.DrawLine(baseLine, 15 * scale, 1 + 14 * scale, Width - 1 - 15 * scale, 1 + 14 * scale);
    }
}

internal sealed class ConnectionBadge : Label
{
    private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch fadeClock = new();
    private float glow;
    private float startGlow;
    private float targetGlow;
    private bool connected;
    private bool requested;

    public (Color Center, Color Edge, Color Groove) UnlitGlass { get; init; } =
        (Color.FromArgb(39, 72, 55), Color.FromArgb(10, 27, 21), Color.FromArgb(72, 107, 83));

    public (Color Center, Color Edge, Color Groove) LitGlass { get; init; } =
        (Color.FromArgb(182, 255, 151), Color.FromArgb(19, 110, 56), Color.FromArgb(20, 81, 44));

    public bool BlinkWhenLit { get; init; }

    public ConnectionBadge()
    {
        fadeTimer.Tick += (_, _) => AdvanceFade();
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void UpdateStatus(string label, bool isConnected, bool isRequested)
    {
        if (Text == label && connected == isConnected && requested == isRequested)
            return;
        Text = label;
        connected = isConnected;
        requested = isRequested;
        var nextGlow = connected && requested ? 1f : 0f;
        if (nextGlow == targetGlow)
            return;

        startGlow = glow;
        targetGlow = nextGlow;
        fadeClock.Restart();
        fadeTimer.Start();
    }

    private void AdvanceFade()
    {
        var duration = targetGlow == 1f ? 420 : 620;
        var hold = targetGlow == 0f ? 500 : 0;
        var elapsed = fadeClock.Elapsed.TotalMilliseconds - hold;
        if (elapsed < 0)
            return;

        if (BlinkWhenLit && targetGlow == 1f && elapsed >= duration)
        {
            const double cycleMs = 2200;
            var phase = (elapsed - duration) % cycleMs / cycleMs;
            glow = (float)((1 + Math.Cos(2 * Math.PI * phase)) / 2);
            Invalidate();
            return;
        }

        var progress = Math.Min(1f, (float)elapsed / duration);
        var eased = progress * progress * (3 - 2 * progress);
        glow = startGlow + (targetGlow - startGlow) * eased;
        if (progress >= 1f)
        {
            fadeTimer.Stop();
            fadeClock.Reset();
        }
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            fadeTimer.Dispose();
        base.Dispose(disposing);
    }

    private Color Blend(Color dark, Color bright)
    {
        int Channel(int from, int to) => (int)Math.Round(from + (to - from) * glow);
        return Color.FromArgb(Channel(dark.A, bright.A), Channel(dark.R, bright.R),
            Channel(dark.G, bright.G), Channel(dark.B, bright.B));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = 48 * scale;
        var bezel = new RectangleF((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
        DrawLamp(e.Graphics, bezel);
    }

    private void DrawLamp(Graphics graphics, RectangleF bezel)
    {
        using var metal = new LinearGradientBrush(bezel,
            Color.FromArgb(214, 222, 219), Color.FromArgb(66, 77, 82), 115f);
        graphics.FillEllipse(metal, bezel);

        var lens = RectangleF.Inflate(bezel, -bezel.Width * .06f, -bezel.Height * .06f);
        using var gasket = new SolidBrush(Color.FromArgb(12, 23, 20));
        graphics.FillEllipse(gasket, lens);
        lens.Inflate(-bezel.Width * .04f, -bezel.Height * .04f);

        using var glassShape = new GraphicsPath();
        glassShape.AddEllipse(lens);
        using var glass = new PathGradientBrush(glassShape)
        {
            CenterColor = Blend(UnlitGlass.Center, LitGlass.Center),
            SurroundColors = [Blend(UnlitGlass.Edge, LitGlass.Edge)]
        };
        graphics.FillEllipse(glass, lens);
        using var groove = new Pen(Blend(Color.FromArgb(110, UnlitGlass.Groove), Color.FromArgb(105, LitGlass.Groove)),
            bezel.Width / 40);
        for (var ring = 1; ring <= 4; ring++)
        {
            var inset = lens.Width * ring / 11;
            graphics.DrawEllipse(groove, RectangleF.Inflate(lens, -inset, -inset));
        }

        using var reflection = new Pen(Color.FromArgb((int)Math.Round(85 + 95 * glow), 215, 255, 223), bezel.Width / 28);
        graphics.DrawArc(reflection, RectangleF.Inflate(lens, -lens.Width / 12, -lens.Height / 12), 207, 86);
    }
}

internal sealed class StudioButton : Button
{
    public bool Primary { get; set; }
    public Color? Swatch { get; set; }

    public StudioButton(string text, string name)
    {
        Text = text;
        AccessibleName = name;
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = Padding.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowStyle.Panel);
        var hovered = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
        var filled = Primary && Enabled;
        var ink = filled ? WindowStyle.Background : Enabled ? WindowStyle.Text : WindowStyle.Muted;
        var scale = DeviceDpi / 96f;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        var edge = Focused && ShowFocusCues ? WindowStyle.Accent : filled ? WindowStyle.Silver : WindowStyle.Border;
        if (Swatch is Color color)
        {
            DrawFuse(g, bounds, scale, color, hovered, filled);
            return;
        }

        if (filled)
            WindowStyle.Satin(g, bounds, 6 * scale, hovered ? Color.White : WindowStyle.Silver,
                Color.FromArgb(142, 159, 178), edge, edge);
        else
            WindowStyle.Satin(g, bounds, 6 * scale, hovered ? Color.FromArgb(45, 56, 68) : Color.FromArgb(37, 46, 56),
                WindowStyle.Field, edge, edge);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void DrawFuse(Graphics g, RectangleF socket, float scale, Color color, bool hovered, bool selected)
    {
        WindowStyle.Satin(g, socket, 5 * scale, Color.FromArgb(10, 13, 16),
            Color.FromArgb(27, 32, 37), Color.FromArgb(49, 57, 63));
        var body = RectangleF.Inflate(socket, -10 * scale, -3 * scale);
        if (body.Width <= 2 || body.Height <= 2)
            return;

        var depth = body;
        depth.Offset(0, 2 * scale);
        WindowStyle.Rounded(g, depth, 4 * scale, Color.FromArgb(5, 7, 9));

        var edge = Focused && ShowFocusCues ? WindowStyle.Accent : selected ? Color.White :
            Color.FromArgb(175, color);
        var top = Color.FromArgb((color.R + 56) / 2, (color.G + 59) / 2, (color.B + 64) / 2);
        var bottom = Color.FromArgb(color.R / 4, color.G / 4, color.B / 4);
        WindowStyle.Satin(g, body, 4 * scale, top, bottom, edge);

        var cap = RectangleF.Inflate(body, -2 * scale, -2 * scale);
        WindowStyle.Satin(g, cap, 3 * scale, Color.FromArgb(185, color),
            Color.FromArgb(55, color), Color.FromArgb(78, Color.White));

        var centerY = cap.Top + cap.Height / 2;
        var leftContact = cap.Left + 5 * scale;
        var rightContact = cap.Right - 5 * scale;
        using var metal = new Pen(Color.FromArgb(105, 223, 218, 198), scale);
        g.DrawLines(metal,
        [
            new PointF(leftContact, centerY),
            new PointF(leftContact + 4 * scale, centerY),
            new PointF(leftContact + 7 * scale, cap.Bottom - 3 * scale),
            new PointF(rightContact - 7 * scale, cap.Bottom - 3 * scale),
            new PointF(rightContact - 4 * scale, centerY),
            new PointF(rightContact, centerY)
        ]);
        using var contact = new SolidBrush(Color.FromArgb(218, 231, 227, 209));
        g.FillRectangle(contact, leftContact - scale, centerY - 2 * scale, 3 * scale, 4 * scale);
        g.FillRectangle(contact, rightContact - 2 * scale, centerY - 2 * scale, 3 * scale, 4 * scale);

        using var reflection = new Pen(Color.FromArgb(135, Color.White), scale);
        g.DrawLine(reflection, cap.Left + 4 * scale, cap.Top + scale, cap.Right - 4 * scale, cap.Top + scale);
        var label = RectangleF.Inflate(cap, -8 * scale, 0);
        var ink = color.GetBrightness() > .68f ? Color.FromArgb(18, 21, 24) : Color.FromArgb(248, 245, 232);
        using var fuseFont = new Font(Font.FontFamily, Font.Size * .88f, Font.Style);
        TextRenderer.DrawText(g, Text, fuseFont, Rectangle.Round(label), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal abstract class DashboardInput : Control
{
    protected DashboardInput(string name, AccessibleRole role)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        AccessibleName = name;
        AccessibleRole = role;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected bool FocusCue => Focused && ShowFocusCues;
}

internal sealed class DriveSelector : DashboardInput
{
    private static readonly PointF Junction = new(0, 1);
    private readonly System.Windows.Forms.Timer motionTimer = new() { Interval = 15 };
    private readonly System.Diagnostics.Stopwatch motionClock = new();
    private PointF start;
    private PointF target;
    private double durationMs;
    private int position;
    private int dragPosition;
    private LightMode lastMode = LightMode.Solid;

    public DriveSelector() : base("Рычаг подсветки", AccessibleRole.Slider)
    {
        motionTimer.Tick += (_, _) =>
        {
            if (motionClock.Elapsed.TotalMilliseconds >= durationMs)
            {
                motionTimer.Stop();
                motionClock.Reset();
                start = target;
                MotionChanged?.Invoke(this, EventArgs.Empty);
            }
            Invalidate();
        };
    }

    public event EventHandler? ValueChanged;
    internal const int NeutralPosition = 7;
    public bool IsNeutral => position == NeutralPosition;
    public bool IsOn => position != 0 && !IsNeutral;
    public bool ChoosingColor => position == 3;
    public bool IsMoving => motionClock.IsRunning;
    public event EventHandler? MotionChanged;

    public LightMode Mode
    {
        get => lastMode;
        set
        {
            if (IsOn)
                Position = value switch { LightMode.Screen => 2, LightMode.Music => 4, LightMode.Magic => 5, LightMode.Chase => 6, _ => 1 };
            else
                lastMode = value;
        }
    }

    public int Position
    {
        get => position;
        set
        {
            value = Math.Clamp(value, 0, 7);
            AnimateTo(value);
            if (position == value)
                return;

            position = value;
            if (IsOn)
                lastMode = position switch
                {
                    2 => LightMode.Screen, 4 => LightMode.Music, 5 => LightMode.Magic,
                    6 => LightMode.Chase, _ => LightMode.Solid
                };

            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static PointF Detent(int value) => value switch
    {
        1 => new(-1, 1),
        2 => new(1, 1),
        3 => new(-1, 0),
        4 => new(-1, 2),
        5 => new(0, 2),
        6 => new(1, 2),
        7 => Junction,
        _ => PointF.Empty
    };

    private PointF HandlePosition
    {
        get
        {
            if (!motionClock.IsRunning)
                return target;

            var progress = Math.Clamp(motionClock.Elapsed.TotalMilliseconds / durationMs, 0, 1);
            var eased = (float)(progress * progress * (3 - 2 * progress));
            return AlongGate(start, target, eased);
        }
    }

    internal static PointF AlongGate(PointF from, PointF to, float progress)
    {
        if (from.X == to.X || (from.Y == Junction.Y && to.Y == Junction.Y))
            return Interpolate(from, to, progress);

        var fromBranch = new PointF(from.X, Junction.Y);
        var toBranch = new PointF(to.X, Junction.Y);
        var firstLeg = Distance(from, fromBranch);
        var middleLeg = Distance(fromBranch, toBranch);
        var lastLeg = Distance(toBranch, to);
        var distance = (firstLeg + middleLeg + lastLeg) * progress;
        if (firstLeg > 0 && distance <= firstLeg)
            return Interpolate(from, fromBranch, distance / firstLeg);

        distance -= firstLeg;
        if (middleLeg > 0 && distance <= middleLeg)
            return Interpolate(fromBranch, toBranch, distance / middleLeg);

        return lastLeg > 0 ? Interpolate(toBranch, to, (distance - middleLeg) / lastLeg) : to;
    }

    private static float Distance(PointF a, PointF b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static PointF Interpolate(PointF a, PointF b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private void AnimateTo(int detent)
    {
        var destination = Detent(detent);
        if (target == destination)
            return;

        var wasMoving = IsMoving;
        start = HandlePosition;
        target = destination;
        durationMs = 120 + 75 * Distance(start, target);
        motionClock.Restart();
        motionTimer.Start();
        if (!wasMoving)
            MotionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.Clear(Parent?.BackColor ?? WindowStyle.Panel);
        WindowStyle.Satin(g, new RectangleF(1, 1, Width - 2, Height - 2), 9 * scale,
            Color.FromArgb(23, 29, 36), Color.FromArgb(12, 16, 21), FocusCue ? WindowStyle.Accent : WindowStyle.Border);

        var center = Width / 2f;
        var reach = Math.Min(94 * scale, Width * .27f);
        var top = 71 * scale;
        var bottom = (Height - 39 * scale + top) / 2;
        if (bottom <= top)
            return;

        PointF Map(PointF p) => new(center + p.X * reach, top + p.Y * (bottom - top));

        var upper = Map(Detent(0));
        var left = Map(Detent(1));
        var right = Map(Detent(2));
        var joint = Map(Junction);
        var palette = Map(Detent(3));
        var highlighted = Capture ? dragPosition : position;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var gate = highlighted == 0 ? Color.FromArgb(63, 75, 86) : Color.FromArgb(30, 42, 54);
        WindowStyle.Satin(g, new RectangleF(upper.X - 14 * scale, upper.Y - 14 * scale, 28 * scale, 28 * scale),
            13 * scale, Color.FromArgb(63, 75, 86), gate, Color.FromArgb(40, 52, 68));
        WindowStyle.Satin(g, new RectangleF(joint.X - 12 * scale, joint.Y - 12 * scale, 24 * scale, 24 * scale),
            10 * scale, Color.FromArgb(63, 75, 86), gate, Color.FromArgb(40, 52, 68));

        using var inlay = new Pen(Color.FromArgb(86, 96, 105), scale);
        g.DrawLine(inlay, upper, joint);
        g.DrawLine(inlay, palette, left);
        g.DrawLine(inlay, left, right);
        g.DrawLine(inlay, left, Map(Detent(4)));
        g.DrawLine(inlay, joint, Map(Detent(5)));
        g.DrawLine(inlay, right, Map(Detent(6)));

        using var rings = new Pen(Color.FromArgb(95, 107, 118), scale);
        using var activeRing = new Pen(highlighted == 1 ? Color.FromArgb(255, 201, 120) : Color.FromArgb(255, 198, 112), 1.5f * scale);
        for (var i = 0; i < 8; i++)
        {
            var detent = Map(Detent(i));
            var ring = new RectangleF(detent.X - 7 * scale, detent.Y - 7 * scale, 14 * scale, 14 * scale);
            g.DrawEllipse(i == highlighted ? activeRing : rings, ring);
        }

        var handle = Map(HandlePosition);
        DrawGearLever(g, handle, scale, highlighted != 0);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, "Выкл", Font,
            Rectangle.Round(new RectangleF(center - 40 * scale, 8 * scale, 80 * scale, 18 * scale)),
            highlighted == 0 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        TextRenderer.DrawText(g, "Цвет", Font,
            Rectangle.Round(new RectangleF(palette.X - 36 * scale, 8 * scale, 72 * scale, 18 * scale)),
            highlighted == 3 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        TextRenderer.DrawText(g, "N", Font,
            Rectangle.Round(new RectangleF(joint.X - 15 * scale, joint.Y + 10 * scale, 30 * scale, 19 * scale)),
            highlighted == 7 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        var half = Width / 2;
        TextRenderer.DrawText(g, "Один цвет", Font,
            new Rectangle(4, (int)(bottom + 10 * scale), half - 8, (int)(19 * scale)),
            highlighted == 1 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        TextRenderer.DrawText(g, "Цвета экрана", Font,
            new Rectangle(half + 4, (int)(bottom + 10 * scale), Width - half - 8, (int)(19 * scale)),
            highlighted == 2 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        for (var i = 4; i <= 6; i++)
        {
            var detent = Map(Detent(i));
            TextRenderer.DrawText(g, ModeName(i), Font,
                Rectangle.Round(new RectangleF(detent.X - reach * .52f, Height - 23 * scale, reach * 1.04f, 19 * scale)),
                highlighted == i ? WindowStyle.Accent : WindowStyle.Muted, flags);
        }
    }

    private static void DrawGearLever(Graphics g, PointF pivot, float scale, bool engaged)
    {
        var collar = new RectangleF(pivot.X - 11 * scale, pivot.Y - 5 * scale, 22 * scale, 11 * scale);
        WindowStyle.Satin(g, collar, 5 * scale, Color.FromArgb(86, 99, 111), Color.FromArgb(19, 24, 31),
            Color.FromArgb(106, 121, 133));

        var stem = new RectangleF(pivot.X - 4 * scale, pivot.Y - 25 * scale, 8 * scale, 28 * scale);
        using var metal = new LinearGradientBrush(stem, Color.FromArgb(73, 85, 97), Color.FromArgb(205, 217, 224), 0f);
        g.FillRectangle(metal, stem);

        var knob = new RectangleF(pivot.X - 18 * scale, pivot.Y - 43 * scale, 36 * scale, 32 * scale);
        var shadow = knob;
        shadow.Offset(2 * scale, 3 * scale);
        WindowStyle.Rounded(g, shadow, 14 * scale, Color.FromArgb(165, 1, 3, 6));
        WindowStyle.Satin(g, knob, 14 * scale, Color.FromArgb(102, 115, 129), Color.FromArgb(21, 26, 34),
            Color.FromArgb(143, 156, 169));
        var cap = RectangleF.Inflate(knob, -4 * scale, -4 * scale);
        WindowStyle.Satin(g, cap, 11 * scale, Color.FromArgb(70, 82, 95), Color.FromArgb(14, 18, 25),
            Color.FromArgb(74, 88, 101));

        var y = cap.Top + cap.Height * .52f;
        var x = pivot.X;
        using var gear = new Pen(engaged ? WindowStyle.Accent : WindowStyle.Muted, 1.35f * scale)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(gear, x - 6 * scale, y, x + 6 * scale, y);
        g.DrawLine(gear, x - 6 * scale, y - 4 * scale, x - 6 * scale, y + 4 * scale);
        g.DrawLine(gear, x + 6 * scale, y - 4 * scale, x + 6 * scale, y + 4 * scale);
        g.DrawLine(gear, x, y - 4 * scale, x, y);
    }

    private int Choose(int x, int y)
    {
        var scale = DeviceDpi / 96f;
        var reach = Math.Min(94 * scale, Width * .27f);
        var middle = (Height + 32 * scale) / 2;
        var lower = Height - 39 * scale;
        if (y >= (middle + lower) / 2)
            return x < Width / 2f - reach / 2 ? 4 : x > Width / 2f + reach / 2 ? 6 : 5;

        if (y < (71 * scale + middle) / 2)
        {
            var paletteBoundary = Width / 2f - Math.Min(94 * DeviceDpi / 96f, Width * .27f) / 2;
            return (position is 1 or 3) && x < paletteBoundary ? 3 : 0;
        }

        var center = Width / 2;
        if (Math.Abs(x - center) <= reach / 2)
            return 7;

        return x < center ? 1 : 2;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        Capture = true;
        dragPosition = Choose(e.X, e.Y);
        AnimateTo(dragPosition);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left)
            return;
        dragPosition = Choose(e.X, e.Y);
        AnimateTo(dragPosition);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && Capture)
        {
            var next = Choose(e.X, e.Y);
            Capture = false;
            Position = next;
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
            AnimateTo(Position);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up: Position = position == 1 ? 3 : 0; break;
            case Keys.Left: Position = 1; break;
            case Keys.Right: Position = 2; break;
            case Keys.Down: Position = position switch { 1 or 3 => 4, 2 => 6, 4 => 5, 5 => 6, 6 => 4, 7 => 5, _ => 7 }; break;
            case Keys.N: Position = NeutralPosition; break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new DriveAccess(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            motionTimer.Dispose();
        base.Dispose(disposing);
    }

    private static string ModeName(int value) => value switch
    {
        0 => "Выключено", 1 => "Один цвет", 2 => "Цвета экрана", 3 => "Выбор цвета",
        4 => "Музыка", 5 => "Волшебный", 6 => "Бегущие огни", 7 => "Нейтраль N", _ => ""
    };

    private sealed class DriveAccess(DriveSelector owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => ModeName(owner.Position);
            set
            {
                if (value == "Выключено") owner.Position = 0;
                else if (value == "Один цвет") owner.Position = 1;
                else if (value == "Цвета экрана") owner.Position = 2;
                else if (value == "Выбор цвета" && owner.Position is 1 or 3) owner.Position = 3;
                else if (value == "Музыка") owner.Position = 4;
                else if (value == "Волшебный") owner.Position = 5;
                else if (value == "Бегущие огни") owner.Position = 6;
                else if (value == "Нейтраль N") owner.Position = NeutralPosition;
            }
        }
    }
}

internal sealed class BrightnessSlider : Control
{
    private readonly System.Windows.Forms.Timer slide = new() { Interval = 15 };
    private readonly System.Diagnostics.Stopwatch slideClock = new();
    private double slideFrom;
    private int value;
    internal double DisplayedValue { get; private set; }
    public event EventHandler? ValueChanged;
    public int Value
    {
        get => value;
        set => SetValue(value, animate: true);
    }

    private void SetValue(int level, bool animate)
    {
        var next = Math.Clamp(level, 0, 100);
        if (!animate || !IsHandleCreated || !Visible || !SystemInformation.UIEffectsEnabled)
        {
            slide.Stop();
            DisplayedValue = next;
            Invalidate();
        }
        else if (next != value)
        {
            slideFrom = DisplayedValue;
            slideClock.Restart();
            slide.Start();
        }

        if (value == next)
            return;
        value = next;
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public BrightnessSlider()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Margin = Padding.Empty;
        AccessibleName = "Яркость";
        AccessibleRole = AccessibleRole.Slider;
        slide.Tick += (_, _) =>
        {
            var progress = Math.Min(1, slideClock.Elapsed.TotalMilliseconds / 160);
            var eased = 1 - Math.Pow(1 - progress, 3);
            DisplayedValue = slideFrom + (value - slideFrom) * eased;
            if (progress >= 1)
                slide.Stop();
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            slide.Dispose();
        base.Dispose(disposing);
    }

    private float Inset => 19 * DeviceDpi / 96f;

    private void Adjust(int steps) => Value += steps;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowStyle.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var width = Math.Max(1, Width - Inset * 2);
        var y = Height / 2f;
        var x = Inset + width * (float)DisplayedValue / 100;

        var recess = new RectangleF(Inset - 3 * scale, y - 4 * scale, width + 6 * scale, 8 * scale);
        WindowStyle.Satin(g, recess, 4 * scale, Color.FromArgb(18, 24, 32), Color.FromArgb(35, 45, 57),
            Color.FromArgb(25, 33, 43));

        var track = new RectangleF(Inset, y - 1.5f * scale, width, 3 * scale);
        using (var trackPath = WindowStyle.RoundedPath(track, 1.5f * scale))
        using (var trackBrush = new LinearGradientBrush(track, Color.FromArgb(69, 81, 94), Color.FromArgb(43, 53, 66), 90f))
            g.FillPath(trackBrush, trackPath);

        var litWidth = x - Inset;
        if (litWidth > .5f)
        {
            DrawWarmTrack(g, new RectangleF(Inset, y - 7 * scale, litWidth, 14 * scale), scale,
                Color.FromArgb(18, 246, 160, 66));
            DrawWarmTrack(g, new RectangleF(Inset, y - 4.5f * scale, litWidth, 9 * scale), scale,
                Color.FromArgb(33, 251, 174, 84));
            DrawWarmTrack(g, new RectangleF(Inset, y - 2.5f * scale, litWidth, 5 * scale), scale,
                Color.FromArgb(42, 254, 185, 103));
            DrawWarmTrack(g, new RectangleF(Inset, y - 1.5f * scale, litWidth, 3 * scale), scale,
                Color.FromArgb(224, 255, 194, 116));

            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse(x - 12 * scale, y - 11 * scale, 24 * scale, 22 * scale);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb((int)Math.Min(63, litWidth / scale * 5), 252, 171, 78),
                SurroundColors = [Color.FromArgb(0, 252, 171, 78)]
            };
            g.FillPath(glow, glowPath);
        }

        var thumb = new RectangleF(x - 5.5f * scale, y - 5.5f * scale, 11 * scale, 11 * scale);
        using var metal = new LinearGradientBrush(thumb, Color.FromArgb(220, 226, 228), Color.FromArgb(79, 93, 105), 90f);
        g.FillEllipse(metal, thumb);
        var focused = Focused && ShowFocusCues;
        using var rim = new Pen(focused ? WindowStyle.Accent : Color.FromArgb(71, 85, 98), scale);
        g.DrawEllipse(rim, focused ? RectangleF.Inflate(thumb, scale, scale) : thumb);
    }

    private static void DrawWarmTrack(Graphics g, RectangleF bounds, float scale, Color color)
    {
        var fade = Math.Min(.32f, 12 * scale / bounds.Width);
        var strength = Math.Min(1, bounds.Width / (8 * scale));
        var clear = Color.FromArgb(0, color);
        using var outline = WindowStyle.RoundedPath(bounds, bounds.Height / 2);
        using var brush = new LinearGradientBrush(bounds, clear, clear, 0f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [clear, Color.FromArgb((int)(color.A * strength * .62f), color),
                    Color.FromArgb((int)(color.A * strength), color), clear],
                Positions = [0, fade, 1 - fade, 1]
            }
        };
        g.FillPath(brush, outline);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        Capture = true;
        ApplyPosition(e.X, animate: true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
            ApplyPosition(e.X, animate: false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            Capture = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Adjust(-1); break;
            case Keys.Right or Keys.Up: Adjust(1); break;
            case Keys.Home: Value = 0; break;
            case Keys.End: Value = 100; break;
            case Keys.PageUp: Adjust(1); break;
            case Keys.PageDown: Adjust(-1); break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibility(this);

    private void ApplyPosition(int x, bool animate) =>
        SetValue((int)Math.Round(100 * (x - Inset) / Math.Max(1, Width - Inset * 2)), animate);

    private sealed class SliderAccessibility(BrightnessSlider slider) : ControlAccessibleObject(slider)
    {
        public override string? Value
        {
            get => slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (int.TryParse(value, out var level)) slider.Value = level; }
        }
    }
}
