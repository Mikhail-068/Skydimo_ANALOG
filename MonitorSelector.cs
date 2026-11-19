using static Kontur.WindowStyle;

namespace Kontur;

internal sealed class MonitorSelector : UserControl
{
    private readonly Panel scheme = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Label details = Label("", 8.5f, color: Muted);
    private readonly System.Windows.Forms.Timer glowTimer = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch glowClock = new();
    private Screen[] displays = [];
    private DisplayButton[] buttons = [];
    public Screen? SelectedDisplay { get; private set; }
    public event EventHandler? SelectionChanged;

    public MonitorSelector()
    {
        AccessibleName = "Подключённые мониторы";
        Margin = Padding.Empty;
        BackColor = Color.Transparent;
        details.BackColor = Color.Transparent;
        details.Dock = DockStyle.Bottom;
        details.Visible = false;
        details.Height = 23;
        Controls.Add(scheme);
        Controls.Add(details);
        scheme.Resize += (_, _) => ArrangeDisplays();
        glowTimer.Tick += (_, _) =>
        {
            var elapsed = glowClock.Elapsed.TotalMilliseconds;
            glowClock.Restart();
            var animating = false;
            foreach (var button in buttons)
                animating |= button.AdvanceGlow((float)(elapsed / 420));
            if (!animating)
                glowTimer.Stop();
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible)
                StartGlow();
            else
            {
                glowTimer.Stop();
                foreach (var button in buttons)
                    button.ResetGlow();
            }
        };
        Disposed += (_, _) => glowTimer.Dispose();
    }

    public void RefreshDisplays(string? preferred)
    {
        var current = Screen.AllScreens;
        if (current.Length == displays.Length && current.Zip(displays).All(pair =>
            pair.First.DeviceName == pair.Second.DeviceName && pair.First.Bounds == pair.Second.Bounds
            && pair.First.Primary == pair.Second.Primary))
            return;

        displays = current;
        scheme.SuspendLayout();
        scheme.AutoScrollPosition = Point.Empty;
        scheme.Controls.Clear();
        foreach (var button in buttons)
            button.Dispose();

        buttons = displays.Select((screen, index) =>
        {
            var button = new DisplayButton(screen, index + 1);
            button.Click += (_, _) => SelectDisplay(button.Screen);
            return button;
        }).ToArray();
        scheme.Controls.AddRange(buttons);
        ArrangeDisplays();
        scheme.ResumeLayout();
        SelectDisplay(displays.FirstOrDefault(screen => screen.DeviceName == preferred)
            ?? displays.FirstOrDefault(screen => screen.Primary) ?? displays.FirstOrDefault());
    }

    private void SelectDisplay(Screen? screen)
    {
        var previous = SelectedDisplay;
        SelectedDisplay = screen;
        foreach (var button in buttons)
            button.Checked = button.Screen == screen;
        if (Visible)
            StartGlow();
        details.Visible = screen is null;
        details.Text = "Мониторы не найдены";
        if (screen?.DeviceName != previous?.DeviceName || screen?.Bounds != previous?.Bounds)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StartGlow()
    {
        glowClock.Restart();
        glowTimer.Start();
    }

    private void ArrangeDisplays()
    {
        if (buttons.Length == 0 || scheme.ClientSize.Width <= 1 || scheme.ClientSize.Height <= 1)
            return;

        var dpi = DeviceDpi / 96f;
        var gap = (int)(8 * dpi);
        var row = (int)(158 * dpi);
        var extent = gap + buttons.Length * (row + gap);
        scheme.SuspendLayout();
        scheme.AutoScroll = extent > scheme.ClientSize.Height;
        scheme.AutoScrollMinSize = new Size(0, scheme.AutoScroll ? extent : 0);
        var scroll = scheme.AutoScrollPosition;
        var width = Math.Max(1, Math.Min((int)(208 * dpi),
            scheme.ClientSize.Width - 2 * gap - (scheme.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0)));
        var x = Math.Max(gap, (scheme.ClientSize.Width - width) / 2);
        for (var i = 0; i < buttons.Length; i++)
            buttons[i].Bounds = new Rectangle(x, scroll.Y + gap + i * (row + gap), width, row);
        scheme.ResumeLayout();
    }

    internal static void DrawPanelSurface(Graphics g, Size size, float dpi)
    {
        var frame = new RectangleF(.5f, .5f, size.Width - 1, size.Height - 1);
        Satin(g, frame, 6 * dpi, Color.FromArgb(55, 62, 66),
            Color.FromArgb(18, 22, 25), Color.FromArgb(86, 93, 95));
        DrawDashboardPlastic(g, RectangleF.Inflate(frame, -3 * dpi, -3 * dpi), dpi, shadeOpacity: 38);
    }

    private sealed class DisplayButton : CheckBox
    {
        public Screen Screen { get; }
        private static readonly Color[] StandColors =
        [
            Color.FromArgb(179, 39, 53), Active, Color.FromArgb(255, 176, 74), Color.FromArgb(110, 151, 255),
            Color.FromArgb(192, 124, 237), Color.FromArgb(239, 124, 175)
        ];
        private readonly Color standColor;
        private readonly AmbientWall ambient = new();
        private readonly Rgb[] glowFrame;
        private float glowLevel;
        private readonly string number;
        private readonly string resolution;


        public DisplayButton(Screen screen, int index)
        {
            Screen = screen;
            standColor = StandColors[(index - 1) % StandColors.Length];
            glowFrame = Enumerable.Repeat(Sk0127.Correct(Rgb.FromColor(standColor), 57), Sk0127.Count).ToArray();
            Disposed += (_, _) => ambient.Dispose();
            var device = screen.DeviceName[(screen.DeviceName.LastIndexOf('\\') + 1)..];
            AccessibleName = $"Монитор {index} · {device} · {screen.Bounds.Width} × {screen.Bounds.Height}";
            number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            resolution = $"{screen.Bounds.Width} × {screen.Bounds.Height}";
            AutoCheck = false;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);

        }

        public bool AdvanceGlow(float step)
        {
            var next = Checked ? Math.Min(1, glowLevel + step) : Math.Max(0, glowLevel - step);
            if (next != glowLevel)
            {
                glowLevel = next;
                Invalidate();
            }
            return glowLevel != (Checked ? 1 : 0);
        }

        public void ResetGlow()
        {
            glowLevel = 0;
            Invalidate();
        }

        private void PaintPanelBackground(PaintEventArgs e)
        {
            if (FindForm() is not Form panel)
            {
                base.OnPaintBackground(e);
                return;
            }

            var origin = PointToClient(panel.PointToScreen(Point.Empty));
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(origin.X, origin.Y);
            DrawPanelSurface(e.Graphics, panel.ClientSize, panel.DeviceDpi / 96f);
            e.Graphics.Restore(state);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintPanelBackground(e);
            var g = e.Graphics;

            var dpi = DeviceDpi / 96f;
            var scale = dpi * .36f;
            var bodyWidth = Math.Min(136 * dpi, Width * .70f);
            var casing = new RectangleF((Width - bodyWidth) / 2, 32 * dpi, bodyWidth, bodyWidth * 9 / 16);
            if (casing.Width < 4 || casing.Height < 4)
                return;
            if (glowLevel > 0)
            {
                var eased = glowLevel * glowLevel * (3 - 2 * glowLevel);
                var spread = casing.Width * .028f;
                var rail = RectangleF.FromLTRB(casing.Left - spread, casing.Top - spread,
                    casing.Right + spread, casing.Bottom);
                ambient.Draw(g, ClientRectangle, rail, glowFrame, eased, featherEdges: true);
            }
            LedPreview.DrawStand(g, casing, scale, standColor);
            Satin(g, casing, 3 * dpi, Color.FromArgb(59, 66, 74), Color.FromArgb(29, 35, 42),
                Focused && ShowFocusCues ? Accent : Color.FromArgb(106, 119, 133));
            var glass = RectangleF.FromLTRB(casing.Left + 2 * dpi, casing.Top + 2 * dpi,
                casing.Right - 2 * dpi, casing.Bottom - 5 * dpi);
            Satin(g, glass, 2 * dpi, Color.FromArgb(31, 46, 65), Color.FromArgb(10, 16, 25), Background);
            using var rib = new Pen(Color.FromArgb(72, 79, 86), .5f * dpi);
            for (var x = glass.Left + dpi; x < glass.Right; x += 2 * dpi)
                g.DrawLine(rib, x, glass.Bottom + dpi, x, casing.Bottom - dpi);
            const TextFormatFlags format = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            using var largeNumber = new Font(Font.FontFamily, Font.Size * 1.65f, FontStyle.Bold);
            var numberWidth = TextRenderer.MeasureText(g, number, largeNumber, Size.Empty, format).Width;
            var resolutionWidth = TextRenderer.MeasureText(g, resolution, Font, Size.Empty, format).Width;
            var gap = (int)Math.Round(8 * dpi);
            var caption = Rectangle.Round(glass);
            var left = caption.Left + (caption.Width - numberWidth - gap - resolutionWidth) / 2;
            TextRenderer.DrawText(g, number, largeNumber, new Rectangle(left, caption.Y, numberWidth, caption.Height),
                Color.FromArgb(255, 216, 85), format);
            TextRenderer.DrawText(g, resolution, Font,
                new Rectangle(left + numberWidth + gap, caption.Y, resolutionWidth, caption.Height),
                Color.White, format);
        }
    }
}
