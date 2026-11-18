using System.Drawing.Drawing2D;
using static Kontur.WindowStyle;

namespace Kontur;

internal sealed class MonitorSelector : UserControl
{
    private readonly Panel scheme = new() { Dock = DockStyle.Fill, BackColor = Field };
    private readonly Label details = Label("", 8.5f, color: Muted);
    private Screen[] displays = [];
    private DisplayButton[] buttons = [];
    public Screen? SelectedDisplay { get; private set; }
    public event EventHandler? SelectionChanged;

    public MonitorSelector()
    {
        AccessibleName = "Подключённые мониторы";
        Margin = Padding.Empty;
        BackColor = Field;
        details.Dock = DockStyle.Bottom;
        details.Height = 23;
        Controls.Add(scheme);
        Controls.Add(details);
        scheme.Resize += (_, _) => ArrangeDisplays();
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
        var selected = buttons.FirstOrDefault(button => button.Screen == screen);
        details.Text = selected is null ? "Мониторы не найдены" : selected.AccessibleName;
        if (screen?.DeviceName != previous?.DeviceName || screen?.Bounds != previous?.Bounds)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ArrangeDisplays()
    {
        if (buttons.Length == 0 || scheme.ClientSize.Width <= 1 || scheme.ClientSize.Height <= 1)
            return;

        var dpi = DeviceDpi / 96f;
        var desktop = displays.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
        var viewport = new Rectangle(Point.Empty, scheme.Size);
        viewport.Inflate(-(int)(8 * dpi), -(int)(3 * dpi));
        var scale = Math.Min(viewport.Width / (float)desktop.Width, viewport.Height / (float)desktop.Height);
        var originX = viewport.Left + (viewport.Width - desktop.Width * scale) / 2;
        var originY = viewport.Top + (viewport.Height - desktop.Height * scale) / 2;
        var positions = displays.Select(screen =>
        {
            var box = screen.Bounds;
            return Rectangle.Round(new RectangleF(originX + (box.Left - desktop.Left) * scale,
                originY + (box.Top - desktop.Top) * scale, box.Width * scale, box.Height * scale));
        }).ToArray();

        var fits = positions.All(box => box.Width >= 43 * dpi && box.Height >= 29 * dpi)
            && positions.SelectMany((box, i) => positions.Skip(i + 1).Select(other => box.IntersectsWith(other))).All(overlap => !overlap);

        scheme.SuspendLayout();
        if (fits)
        {
            scheme.AutoScrollPosition = Point.Empty;
            scheme.AutoScrollMinSize = Size.Empty;
            scheme.AutoScroll = false;
            for (var i = 0; i < positions.Length; i++)
            {
                var box = positions[i];
                box.Inflate(-(int)(3 * dpi), -(int)(2 * dpi));
                buttons[i].Bounds = box;
            }
        }
        else
        {
            var gap = (int)(6 * dpi);
            var cell = new Size((int)(63 * dpi), (int)(42 * dpi));
            var columns = Math.Max(1, (viewport.Width + gap) / (cell.Width + gap));
            var rows = (buttons.Length + columns - 1) / columns;
            var needsScroll = gap + rows * (cell.Height + gap) > scheme.Height;
            if (needsScroll)
            {
                columns = Math.Max(1, (viewport.Width - SystemInformation.VerticalScrollBarWidth + gap) / (cell.Width + gap));
                rows = (buttons.Length + columns - 1) / columns;
            }
            else
            {
                scheme.AutoScrollPosition = Point.Empty;
            }

            scheme.AutoScroll = needsScroll;
            scheme.AutoScrollMinSize = needsScroll ? new Size(0, gap + rows * (cell.Height + gap)) : Size.Empty;
            var offset = scheme.AutoScrollPosition;
            var top = needsScroll ? gap : (scheme.Height - rows * cell.Height - (rows - 1) * gap) / 2;
            for (var i = 0; i < buttons.Length; i++)
            {
                var count = Math.Min(columns, buttons.Length - (i / columns) * columns);
                var rowWidth = count * cell.Width + (count - 1) * gap;
                var left = Math.Max(0, (scheme.ClientSize.Width - rowWidth) / 2);
                var location = new Point(left + i % columns * (cell.Width + gap), top + i / columns * (cell.Height + gap));
                location.Offset(offset);
                buttons[i].Bounds = new Rectangle(location, cell);
            }
        }
        scheme.ResumeLayout();
    }

    private sealed class DisplayButton : CheckBox
    {
        public Screen Screen { get; }
        private readonly string number;

        public DisplayButton(Screen screen, int index)
        {
            Screen = screen;
            number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AccessibleName = $"Монитор {index} · {screen.Bounds.Width} × {screen.Bounds.Height}";
            AutoCheck = false;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Field);
            var dpi = DeviceDpi / 96f;
            var casing = new RectangleF(dpi, dpi, Width - 2 * dpi, Height - 7 * dpi);
            var rim = Checked ? Active : Border;
            using var foot = new Pen(rim, dpi);
            var center = Width / 2f;
            g.DrawLine(foot, center, casing.Bottom, center, Height - 2 * dpi);
            g.DrawLine(foot, center - 9 * dpi, Height - 2 * dpi, center + 9 * dpi, Height - 2 * dpi);
            var hovering = ClientRectangle.Contains(PointToClient(MousePosition));
            Satin(g, casing, 4 * dpi, Checked ? Color.FromArgb(52, 85, 93) : hovering ? Color.FromArgb(53, 66, 81) : Color.FromArgb(36, 49, 63),
                Checked ? Color.FromArgb(22, 51, 57) : WindowStyle.Panel, Focused && ShowFocusCues ? Accent : rim);
            TextRenderer.DrawText(g, number, Font, Rectangle.Round(casing), Checked ? Active : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }
}

