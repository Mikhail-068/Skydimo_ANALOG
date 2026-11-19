using System.Drawing.Drawing2D;

namespace Kontur;

internal sealed class ShadeDialog : Form
{
    private readonly NumericUpDown[] rgb = new NumericUpDown[3];
    private readonly Wheel wheel;
    private bool synchronizing;
    private readonly System.Windows.Forms.Timer slide = new() { Interval = 15 };
    private readonly System.Diagnostics.Stopwatch clock = new();
    private double position;
    private double from;
    private bool closing;

    public bool IsRetracting => closing;
    public event Action<Color>? ShadeAccepted;

    public event Action<Color>? PreviewChanged;

    public Color Shade => Color.FromArgb((int)rgb[0].Value, (int)rgb[1].Value, (int)rgb[2].Value);

    public ShadeDialog(Color initial)
    {
        Text = "Выбор цвета";
        ClientSize = new Size(400, 208);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = WindowStyle.Panel;
        ForeColor = WindowStyle.Text;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Retract();
            }
        };

        wheel = new Wheel(initial) { Bounds = new Rectangle(14, 10, 188, 188), AccessibleName = "Кольцо оттенков" };
        Controls.Add(wheel);
        wheel.ShadeChanged += shade =>
        {
            synchronizing = true;
            rgb[0].Value = shade.R;
            rgb[1].Value = shade.G;
            rgb[2].Value = shade.B;
            synchronizing = false;
            PreviewChanged?.Invoke(Shade);
        };

        int[] channels = [initial.R, initial.G, initial.B];
        for (var i = 0; i < rgb.Length; i++)
        {
            var name = "RGB"[i].ToString();
            var y = 25 + i * 35;
            Controls.Add(new Label { Text = name, Bounds = new Rectangle(218, y, 23, 25), TextAlign = ContentAlignment.MiddleLeft });
            var field = new NumericUpDown
            {
                Bounds = new Rectangle(247, y, 128, 25),
                Maximum = 255, Value = channels[i], AccessibleName = name,
                BackColor = WindowStyle.Background, ForeColor = WindowStyle.Text
            };
            rgb[i] = field;
            Controls.Add(field);
            field.ValueChanged += (_, _) =>
            {
                if (synchronizing)
                    return;
                wheel.SetShade(Shade);
                PreviewChanged?.Invoke(Shade);
            };
        }

        var apply = new StudioButton("Выбрать", "Применить цвет")
        {
            Bounds = new Rectangle(212, 149, 163, 34), Primary = true
        };
        apply.Click += (_, _) =>
        {
            if (closing)
                return;
            ShadeAccepted?.Invoke(Shade);
            Retract();
        };
        Controls.Add(apply);
        AcceptButton = apply;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        WindowStyle.DrawDashboardPlastic(e.Graphics,
            new RectangleF(.5f, .5f, ClientSize.Width - 1, ClientSize.Height - 1), DeviceDpi / 96f);
    }

    public void ShowBeside(Form owner, Control anchor)
    {
        void Place()
        {
            var work = Screen.FromControl(owner).WorkingArea;
            var seam = owner.RectangleToScreen(owner.ClientRectangle).Right - 1;
            var x = Math.Clamp(seam, work.Left, Math.Max(work.Left, work.Right - Width));
            var anchorBounds = anchor.RectangleToScreen(anchor.ClientRectangle);
            var y = Math.Clamp(anchorBounds.Top + (anchorBounds.Height - Height) / 2, work.Top,
                Math.Max(work.Top, work.Bottom - Height));
            var beside = x == seam;
            var travel = beside ? Width - 1 : 32 * DeviceDpi / 96;
            Left = x - (int)Math.Round(travel * (1 - position));
            Top = y;

            var inset = beside ? x - Left : 0;
            var previous = Region;
            Region = inset > 0 ? new Region(new Rectangle(inset, 0, Width - inset, Height)) : null;
            previous?.Dispose();
            Opacity = beside ? 1 : position;
        }

        void FollowOwner(object? sender, EventArgs e) => Place();
        owner.LocationChanged += FollowOwner;
        owner.SizeChanged += FollowOwner;
        FormClosed += (_, _) =>
        {
            owner.LocationChanged -= FollowOwner;
            owner.SizeChanged -= FollowOwner;
        };
        slide.Tick += (_, _) =>
        {
            var progress = Math.Min(clock.Elapsed.TotalMilliseconds / (closing ? 300 : 420), 1);
            var eased = progress * progress * (3 - 2 * progress);
            position = closing ? from * (1 - eased) : eased;
            Place();
            if (progress < 1)
                return;

            slide.Stop();
            if (closing)
                Close();
        };

        Place();
        Show(owner);
        clock.Restart();
        slide.Start();
    }

    public void Retract()
    {
        if (closing)
            return;
        closing = true;
        from = position;
        clock.Restart();
        slide.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            slide.Dispose();
        base.Dispose(disposing);
    }

    private sealed class Wheel : Control
    {
        private float hue, saturation, value;
        private bool draggingRing;

        public event Action<Color>? ShadeChanged;

        public Wheel(Color initial)
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ResizeRedraw = true;
            SetShade(initial);
        }

        private float Radius => Math.Min(Width, Height) * .44f;
        private RectangleF Square => new(Width / 2f - Radius * .52f, Height / 2f - Radius * .52f, Radius * 1.04f, Radius * 1.04f);

        public void SetShade(Color color)
        {
            var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
            value = maximum / 255f;
            saturation = maximum == 0 ? 0 : (maximum - minimum) / (float)maximum;
            if (saturation > 0)
                hue = color.GetHue();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = Radius;
            if (radius <= 1)
                return;
            var cx = Width / 2f;
            var cy = Height / 2f;
            var ring = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
            using var segment = new Pen(Color.Red, radius * .21f);
            for (var angle = 0; angle < 360; angle += 2)
            {
                segment.Color = FromHsv((360 - angle) % 360, 1, 1);
                g.DrawArc(segment, ring, angle - 1, 3);
            }

            var square = Square;
            using var chroma = new LinearGradientBrush(square, Color.White, FromHsv(hue, 1, 1), LinearGradientMode.Horizontal);
            using var shadow = new LinearGradientBrush(square, Color.FromArgb(0, Color.Black), Color.Black, LinearGradientMode.Vertical);
            g.FillRectangle(chroma, square);
            g.FillRectangle(shadow, square);

            var radians = hue * Math.PI / 180;
            Marker(g, cx + radius * (float)Math.Cos(radians), cy - radius * (float)Math.Sin(radians), radius * .075f);
            Marker(g, square.Left + saturation * square.Width, square.Bottom - value * square.Height, radius * .075f);
        }

        private static void Marker(Graphics g, float x, float y, float radius)
        {
            using var dark = new Pen(Color.Black, 3);
            using var light = new Pen(Color.White, 1.4f);
            var bounds = new RectangleF(x - radius, y - radius, radius * 2, radius * 2);
            g.DrawEllipse(dark, bounds);
            g.DrawEllipse(light, bounds);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            var dx = e.X - Width / 2f;
            var dy = e.Y - Height / 2f;
            draggingRing = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Radius) <= Radius * .15f;
            if (!draggingRing && !Square.Contains(e.Location))
                return;
            Capture = true;
            Pick(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture)
                Pick(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
                Capture = false;
        }

        private void Pick(Point point)
        {
            if (draggingRing)
                hue = (float)((-Math.Atan2(point.Y - Height / 2f, point.X - Width / 2f) * 180 / Math.PI + 360) % 360);
            else
            {
                var square = Square;
                saturation = Math.Clamp((point.X - square.Left) / square.Width, 0, 1);
                value = Math.Clamp((square.Bottom - point.Y) / square.Height, 0, 1);
            }
            ShadeChanged?.Invoke(FromHsv(hue, saturation, value));
            Invalidate();
        }

        private static Color FromHsv(float hue, float saturation, float value)
        {
            int Channel(float offset)
            {
                var position = (offset + hue / 60) % 6;
                var amount = Math.Clamp(Math.Min(position, 4 - position), 0, 1);
                return (int)Math.Round(255 * value * (1 - saturation * amount));
            }

            return Color.FromArgb(Channel(5), Channel(3), Channel(1));
        }
    }
}
