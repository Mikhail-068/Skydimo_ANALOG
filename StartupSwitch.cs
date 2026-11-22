using System.Drawing.Drawing2D;

namespace Kontur;

internal sealed class StartupSwitch : CheckBox
{
    public StartupSwitch()
    {
        Text = "Автозагрузка";
        AccessibleName = Text;
        AccessibleDescription = "Запускать «Контур» при входе в Windows";
        MinimumSize = new Size(224, 30);
        Anchor = AnchorStyles.None;
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        SettingsSurface.PaintField(this, g);
        var scale = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(Width - 57 * scale, (Height - 24 * scale) / 2, 53 * scale, 24 * scale);
        var label = Rectangle.Round(new RectangleF(4 * scale, 0, track.Left - 14 * scale, Height));
        TextRenderer.DrawText(g, Text, Font, label, Enabled ? WindowStyle.Text : WindowStyle.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        WindowStyle.Satin(g, track, 12 * scale,
            Checked ? Color.FromArgb(27, 29, 22) : Color.FromArgb(11, 13, 15),
            Checked ? Color.FromArgb(78, 64, 36) : Color.FromArgb(36, 40, 42),
            Focused && ShowFocusCues ? WindowStyle.Silver : Color.FromArgb(82, 75, 57));

        var inset = 3 * scale;
        var diameter = track.Height - 2 * inset;
        var thumb = new RectangleF(Checked ? track.Right - inset - diameter : track.Left + inset,
            track.Top + inset, diameter, diameter);
        using var rim = new LinearGradientBrush(thumb, Color.FromArgb(242, 218, 149), Color.FromArgb(108, 73, 23), 60);
        using var core = new LinearGradientBrush(thumb,
            Checked ? Color.FromArgb(231, 173, 68) : Color.FromArgb(151, 121, 65),
            Checked ? Color.FromArgb(139, 94, 32) : Color.FromArgb(97, 78, 42), 90);
        g.FillEllipse(rim, thumb);
        g.FillEllipse(core, RectangleF.Inflate(thumb, -1.7f * scale, -1.7f * scale));
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }
}
