using System.Drawing.Drawing2D;

namespace Kontur;

internal sealed class ControllerPicker : Button
{
    internal sealed record Option(string? Port, string Caption);
    private const string Caption = "USB-контроллер";
    private readonly ToolStripDropDownMenu popup = new()
    {
        ShowCheckMargin = false,
        ShowImageMargin = false,
        Renderer = new PortsRenderer()
    };

    internal IReadOnlyList<Option> Options { get; private set; } = [];
    internal Option? Selection { get; private set; }
    internal bool IsOpen => popup.Visible;
    internal event EventHandler? Opening;
    internal event EventHandler? SelectionChanged;

    public ControllerPicker()
    {
        AccessibleName = "COM-порт контроллера";
        AccessibleRole = AccessibleRole.ComboBox;
        MinimumSize = new Size(224, 58);
        Size = MinimumSize;
        Anchor = AnchorStyles.None;
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        popup.Closing += (_, e) =>
        {
            // Let the picker handle its own click after the menu sees the mouse-down.
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked
                && (MouseButtons & MouseButtons.Left) != 0
                && ClientRectangle.Contains(PointToClient(MousePosition)))
            {
                e.Cancel = true;
            }
        };
        popup.Opened += (_, _) => Invalidate();
        popup.Closed += (_, _) => Invalidate();
        popup.SizeChanged += (_, _) =>
        {
            if (popup.Width < 2 || popup.Height < 2)
                return;
            using var outline = WindowStyle.RoundedPath(new RectangleF(0, 0, popup.Width, popup.Height), 7 * ScaleFactor);
            var previous = popup.Region;
            popup.Region = new Region(outline);
            previous?.Dispose();
        };
    }

    private float ScaleFactor => DeviceDpi / 96f;

    internal void ResetOptions(IReadOnlyList<Option> options, string? selectedPort)
    {
        Options = options;
        Selection = options.FirstOrDefault(option => string.Equals(option.Port, selectedPort, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault();
        Text = Selection?.Caption ?? "";
        Invalidate();
    }

    internal void Choose(Option option)
    {
        popup.Close(ToolStripDropDownCloseReason.ItemClicked);
        if (option == Selection)
            return;
        Selection = option;
        Text = option.Caption;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        OpenPorts();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.F4 or Keys.Down || keyData == (Keys.Alt | Keys.Down))
        {
            OpenPorts();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal void ClosePorts() => popup.Close();

    private void OpenPorts()
    {
        if (popup.Visible)
        {
            popup.Close();
            return;
        }

        Opening?.Invoke(this, EventArgs.Empty);
        if (Options.Count == 0)
            return;

        foreach (var item in popup.Items.Cast<ToolStripItem>().ToArray())
            item.Dispose();

        var scale = ScaleFactor;
        popup.MinimumSize = new Size(Width - (int)Math.Round(4 * scale), 0);
        popup.Padding = new Padding((int)Math.Ceiling(4 * scale));
        foreach (var option in Options)
        {
            var item = new ToolStripMenuItem(option.Caption)
            {
                AutoSize = false,
                Width = Width - (int)Math.Round(12 * scale),
                Height = (int)Math.Round(30 * scale),
                Padding = new Padding((int)Math.Round(8 * scale), 0, (int)Math.Round(8 * scale), 0),
                Font = Font,
                ForeColor = WindowStyle.Text,
                Checked = option == Selection
            };
            item.Click += (_, _) => Choose(option);
            popup.Items.Add(item);
        }

        popup.Show(this, new Point((int)Math.Round(2 * scale), Height - (int)Math.Round(2 * scale)));
        popup.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Checked)?.Select();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 60 || Height < 25)
            return;

        var g = e.Graphics;
        SettingsSurface.PaintField(this, g);
        var scale = ScaleFactor;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var frame = RectangleF.FromLTRB(2 * scale, 13 * scale, Width - 2 * scale, Height - 4 * scale);
        using var outline = WindowStyle.RoundedPath(frame, 6 * scale);
        using var line = new Pen(Focused || IsOpen ? WindowStyle.Text : Color.FromArgb(160, 174, 183),
            (Focused || IsOpen ? 1.35f : 1) * scale);
        const TextFormatFlags labelFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        using var captionFont = new Font(Font.FontFamily, 9);
        var title = TextRenderer.MeasureText(g, Caption, captionFont, new Size(Width, Height), labelFlags);
        var label = new Rectangle((int)(15 * scale), (int)Math.Round(frame.Top - title.Height / 2f),
            title.Width + 2, title.Height);
        var gap = Rectangle.Inflate(label, (int)Math.Ceiling(5 * scale), 0);
        var state = g.Save();
        g.ExcludeClip(gap);
        g.DrawPath(line, outline);
        g.Restore(state);
        TextRenderer.DrawText(g, Caption, captionFont, label, WindowStyle.Text, labelFlags);

        var value = Rectangle.Round(RectangleF.FromLTRB(15 * scale, frame.Top + 3 * scale,
            frame.Right - 27 * scale, frame.Bottom - 2 * scale));
        TextRenderer.DrawText(g, Text, Font, value, WindowStyle.Text, labelFlags | TextFormatFlags.EndEllipsis);
        var x = frame.Right - 15 * scale;
        var y = (frame.Top + frame.Bottom) / 2;
        var direction = IsOpen ? -1 : 1;
        PointF[] arrow =
            [new(x - 4 * scale, y - 2 * scale * direction), new(x + 4 * scale, y - 2 * scale * direction),
             new(x, y + 3 * scale * direction)];
        g.FillPolygon(Brushes.White, arrow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            popup.Dispose();
        base.Dispose(disposing);
    }

    private sealed class PortsRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var box = new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            WindowStyle.DrawDashboardPlastic(e.Graphics, box, e.ToolStrip.DeviceDpi / 96f);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var box = new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var outline = WindowStyle.RoundedPath(box, 7 * e.ToolStrip.DeviceDpi / 96f);
            using var stroke = new Pen(Color.FromArgb(125, 139, 147));
            e.Graphics.DrawPath(stroke, outline);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && e.Item is not ToolStripMenuItem { Checked: true })
                return;

            WindowStyle.Rounded(e.Graphics,
                new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2),
                4 * (e.Item.Owner?.DeviceDpi ?? 96) / 96f,
                e.Item.Selected ? Color.FromArgb(76, 84, 87) : Color.FromArgb(57, 65, 67));
        }
    }
}
