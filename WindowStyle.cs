using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kontur;

internal static class WindowStyle
{
    public static readonly Color Background = Color.FromArgb(9, 12, 17);
    public static readonly Color Panel = Color.FromArgb(19, 24, 31);
    public static readonly Color Field = Color.FromArgb(26, 32, 40);
    public static readonly Color Border = Color.FromArgb(62, 74, 87);
    public static readonly Color Text = Color.FromArgb(243, 245, 246);
    public static readonly Color Muted = Color.FromArgb(195, 205, 214);
    public static readonly Color Silver = Color.FromArgb(214, 223, 226);
    public static readonly Color Accent = Color.FromArgb(255, 198, 112);
    public static readonly Color Active = Color.FromArgb(142, 225, 210);

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
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(bounds, radius);
        using var fill = new LinearGradientBrush(bounds, top, bottom, 90f);
        using var outline = new Pen(edge);
        g.FillPath(fill, path);
        g.DrawPath(outline, path);
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
        WindowStyle.Satin(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            13 * scale, Color.FromArgb(36, 45, 55), WindowStyle.Panel, WindowStyle.Border);
        using var trim = new Pen(Color.FromArgb(212, 165, 106), 1.3f * scale);
        e.Graphics.DrawLine(trim, 15 * scale, 1, Math.Min(90 * scale, Width - 15 * scale), 1);
    }
}

internal sealed class ConnectionBadge : Label
{
    private bool connected;
    private bool requested;

    public ConnectionBadge()
    {
        Font = new Font("Segoe UI", 9, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void UpdateStatus(string label, bool isConnected, bool isRequested)
    {
        if (Text == label && connected == isConnected && requested == isRequested)
            return;
        Text = label;
        connected = isConnected;
        requested = isRequested;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.Clear(WindowStyle.Background);
        var tint = connected ? WindowStyle.Active : requested ? WindowStyle.Accent : WindowStyle.Muted;
        var fill = connected ? Color.FromArgb(22, 45, 49) : Color.FromArgb(24, 30, 39);
        WindowStyle.Rounded(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * scale, fill, WindowStyle.Border);
        using var dot = new SolidBrush(tint);
        e.Graphics.FillEllipse(dot, 13 * scale, Height / 2f - 3 * scale, 6 * scale, 6 * scale);
        var inset = (int)Math.Round(25 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(inset, 0, Width - inset - 4, Height), tint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
        if (filled)
            WindowStyle.Satin(g, bounds, 6 * scale, hovered ? Color.White : WindowStyle.Silver, Color.FromArgb(142, 159, 178), edge);
        else
            WindowStyle.Satin(g, bounds, 6 * scale, hovered ? Color.FromArgb(45, 56, 68) : Color.FromArgb(37, 46, 56),
                WindowStyle.Field, edge);
        var textBounds = ClientRectangle;
        var format = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (Swatch is Color color)
        {
            var size = 16 * scale;
            WindowStyle.Rounded(g, new RectangleF(12 * scale, (Height - size) / 2, size, size), 4 * scale, color);
            textBounds.X = (int)(38 * scale);
            textBounds.Width -= textBounds.X + (int)(12 * scale);
            format |= TextFormatFlags.Left;
        }
        else
        {
            format |= TextFormatFlags.HorizontalCenter;
        }
        TextRenderer.DrawText(g, Text, Font, textBounds, ink, format);
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
            }
            Invalidate();
        };
    }

    public event EventHandler? ValueChanged;
    public bool IsOn => position != 0;

    public LightMode Mode
    {
        get => lastMode;
        set
        {
            if (IsOn)
                Position = value == LightMode.Solid ? 1 : 2;
            else
                lastMode = value;
        }
    }

    public int Position
    {
        get => position;
        set
        {
            value = Math.Clamp(value, 0, 2);
            AnimateTo(value);
            if (position == value)
                return;

            position = value;
            if (IsOn)
                lastMode = position == 1 ? LightMode.Solid : LightMode.Screen;

            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static PointF Detent(int value) => value switch
    {
        1 => new(-1, 1),
        2 => new(1, 1),
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
        if (from.X == to.X || from.Y == to.Y)
            return Interpolate(from, to, progress);

        var firstLeg = Distance(from, Junction);
        var distance = (firstLeg + Distance(Junction, to)) * progress;
        if (distance <= firstLeg)
            return Interpolate(from, Junction, distance / firstLeg);

        return Interpolate(Junction, to, (distance - firstLeg) / Distance(Junction, to));
    }

    private static float Distance(PointF a, PointF b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static PointF Interpolate(PointF a, PointF b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private void AnimateTo(int detent)
    {
        var destination = Detent(detent);
        if (target == destination)
            return;

        start = HandlePosition;
        target = destination;
        durationMs = 120 + 75 * Distance(start, target);
        motionClock.Restart();
        motionTimer.Start();
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
        var reach = Math.Min(72 * scale, Width * .22f);
        var top = 71 * scale;
        var bottom = Height - 39 * scale;
        if (bottom <= top)
            return;

        PointF Map(PointF p) => new(center + p.X * reach, top + p.Y * (bottom - top));

        var upper = Map(Detent(0));
        var left = Map(Detent(1));
        var right = Map(Detent(2));
        var joint = Map(Junction);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        PaintGate(Color.FromArgb(63, 75, 86), 13 * scale);
        PaintGate(Color.FromArgb(6, 9, 12), 10 * scale);

        using var inlay = new Pen(Color.FromArgb(86, 96, 105), scale);
        g.DrawLine(inlay, upper, joint);
        g.DrawLine(inlay, left, right);

        void PaintGate(Color ink, float width)
        {
            using var pen = new Pen(ink, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, upper, joint);
            g.DrawLine(pen, left, right);
        }

        using var rings = new Pen(Color.FromArgb(95, 107, 118), scale);
        using var activeRing = new Pen(Color.FromArgb(165, 108, 54), 1.4f * scale);
        var highlighted = Capture ? dragPosition : position;
        for (var i = 0; i < 3; i++)
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
        var half = Width / 2;
        TextRenderer.DrawText(g, "Один цвет", Font,
            new Rectangle(4, (int)(Height - 24 * scale), half - 8, (int)(19 * scale)),
            highlighted == 1 ? WindowStyle.Accent : WindowStyle.Muted, flags);
        TextRenderer.DrawText(g, "Цвета экрана", Font,
            new Rectangle(half + 4, (int)(Height - 24 * scale), Width - half - 8, (int)(19 * scale)),
            highlighted == 2 ? WindowStyle.Accent : WindowStyle.Muted, flags);
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
        if (y < Height * .48f)
            return 0;

        var center = Width / 2;
        if (Math.Abs(x - center) <= 6 * DeviceDpi / 96)
            return Mode == LightMode.Solid ? 1 : 2;

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
            Position = Choose(e.X, e.Y);
            Capture = false;
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
            case Keys.Up: Position = 0; break;
            case Keys.Left: Position = 1; break;
            case Keys.Right: Position = 2; break;
            case Keys.Down: Position = Mode == LightMode.Solid ? 1 : 2; break;
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

    private sealed class DriveAccess(DriveSelector owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => owner.Position switch { 0 => "Выключено", 1 => "Один цвет", _ => "Цвета экрана" };
            set
            {
                if (value == "Выключено") owner.Position = 0;
                else if (value == "Один цвет") owner.Position = 1;
                else if (value == "Цвета экрана") owner.Position = 2;
            }
        }
    }
}

internal sealed class BrightnessSlider : Control
{
    private int value;
    public event EventHandler? ValueChanged;
    public int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (this.value == next)
                return;
            this.value = next;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public BrightnessSlider()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Margin = Padding.Empty;
        AccessibleName = "Яркость";
        AccessibleRole = AccessibleRole.Slider;
    }

    private float Inset => 19 * DeviceDpi / 96f;

    private void Adjust(int steps) => Value += steps;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowStyle.Panel);
        var scale = DeviceDpi / 96f;
        var width = Math.Max(1, Width - Inset * 2);
        var y = Height / 2f;
        var x = Inset + width * Value / 100;
        var track = new RectangleF(Inset, y - 2 * scale, width, 4 * scale);
        WindowStyle.Rounded(g, track, 2 * scale, Color.FromArgb(55, 65, 76));
        if (x - Inset > 1)
            WindowStyle.Rounded(g, new RectangleF(Inset, track.Y, x - Inset, track.Height), 2 * scale, WindowStyle.Accent);

        var thumb = new RectangleF(x - 6 * scale, y - 6 * scale, 12 * scale, 12 * scale);
        WindowStyle.Rounded(g, thumb, 6 * scale, WindowStyle.Silver,
            Focused && ShowFocusCues ? WindowStyle.Accent : WindowStyle.Panel);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        Capture = true;
        ApplyPosition(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
            ApplyPosition(e.X);
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

    private void ApplyPosition(int x) =>
        Value = (int)Math.Round(100 * (x - Inset) / Math.Max(1, Width - Inset * 2));

    private sealed class SliderAccessibility(BrightnessSlider slider) : ControlAccessibleObject(slider)
    {
        public override string? Value
        {
            get => slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (int.TryParse(value, out var level)) slider.Value = level; }
        }
    }
}
