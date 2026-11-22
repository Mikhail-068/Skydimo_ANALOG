namespace Kontur;

internal sealed class SettingsSurface : TableLayoutPanel
{
    private readonly Control upperPanel;
    private Bitmap? texture;
    private int textureTop;
    private int textureDpi;

    public SettingsSurface(Control upperPanel)
    {
        this.upperPanel = upperPanel;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PaintTexture(e.Graphics, Point.Empty);
    }

    internal static void PaintField(Control field, Graphics g)
    {
        using var back = new SolidBrush(WindowStyle.Field);
        g.FillRectangle(back, field.ClientRectangle);
        if (field.Parent is SettingsSurface panel)
            panel.PaintTexture(g, field.Location);
    }

    private void PaintTexture(Graphics g, Point origin)
    {
        int top = upperPanel.Bottom + 1;
        var size = new Size(ClientSize.Width, ClientSize.Height - top);
        if (size.Width <= 2 || size.Height <= 2)
            return;

        if (texture is null || texture.Size != size || textureTop != top || textureDpi != DeviceDpi)
        {
            texture?.Dispose();
            texture = new Bitmap(size.Width, size.Height);
            textureTop = top;
            textureDpi = DeviceDpi;

            using var surface = Graphics.FromImage(texture);
            surface.TranslateTransform(0, -top);
            WindowStyle.DrawDashboardPlastic(surface,
                RectangleF.FromLTRB(.5f, top, ClientSize.Width - .5f, ClientSize.Height - .5f),
                DeviceDpi / 96f, shadeOpacity: 38);
        }

        // Both the panel and its child controls sample the same pixels.
        g.DrawImageUnscaled(texture, -origin.X, top - origin.Y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            texture?.Dispose();
        base.Dispose(disposing);
    }
}
