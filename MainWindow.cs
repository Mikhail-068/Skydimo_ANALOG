using static Kontur.WindowStyle;

namespace Kontur;

internal sealed class MainWindow : Form
{
    private readonly LocalFiles files;
    private readonly LightingEngine engine;
    private readonly bool backgroundPreview;

    private readonly MonitorSelector monitor = new();
    private readonly DriveSelector drive = new();
    private readonly CheckBox reverse = new() { Text = "Лента подключена справа", Margin = Padding.Empty };
    private readonly BrightnessSlider brightness = new();
    private readonly Label brightnessValue = Label("", 15, color: Silver, font: "Bahnschrift");
    private readonly Label powerState = Label("", 9, color: Muted);
    private ShadeDialog? shadeDrawer;
    private readonly ConnectionBadge badge = new();
    private readonly ConnectionBadge screenLamp = new()
    {
        AccessibleName = "Синяя лампа режима «Цвета экрана»",
        LitGlass = (Color.FromArgb(135, 200, 255), Color.FromArgb(25, 75, 186), Color.FromArgb(20, 57, 134)),
        UnlitGlass = (Color.FromArgb(31, 49, 81), Color.FromArgb(11, 19, 35), Color.FromArgb(55, 80, 125))
    };
    private readonly ConnectionBadge colorLamp = new()
    {
        AccessibleName = "Красная лампа выбора цвета",
        BlinkWhenLit = true,
        LitGlass = (Color.FromArgb(255, 145, 126), Color.FromArgb(168, 30, 34), Color.FromArgb(120, 26, 29)),
        UnlitGlass = (Color.FromArgb(81, 30, 35), Color.FromArgb(34, 12, 16), Color.FromArgb(125, 54, 59))
    };
    private readonly Label status = Label("", 9, color: Muted);
    private readonly Label captureStatus = Label("", 9, color: Muted);
    private readonly TableLayoutPanel colors = Table(3);

    private readonly LedPreview preview = new();
    private readonly TableLayoutPanel screenSettings = Table(1);
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer save = new() { Interval = 350 };
    private Color color;
    private readonly Dictionary<int, int> paletteOverrides;
    private int? paletteSlot;
    private StudioButton? customColor;
    private int displayRefresh;
    private string savedPort = "COM12";

    public MainWindow(LocalFiles files, bool backgroundPreview = false)
    {
        this.files = files;
        this.backgroundPreview = backgroundPreview;
        var settings = files.Load();
        SuspendLayout();
        Text = "Контур — подсветка SK0127";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1060, 650);
        MinimumSize = new Size(900, 648);
        BackColor = Background;
        ForeColor = WindowStyle.Text;
        Font = new Font("Segoe UI", 10);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        Controls.Add(BuildLayout());
        monitor.RefreshDisplays(settings.Monitor);
        savedPort = settings.Port;
        brightness.Value = settings.Brightness;
        drive.Mode = settings.Mode;
        reverse.Checked = settings.Reverse;
        preview.ReverseStrip = reverse.Checked;
        preview.StripPath = settings.StripPath is { Length: Sk0127.Count } path ? path : null;
        reverse.Enabled = preview.StripPath is null;
        color = Color.FromArgb(settings.ColorArgb);
        paletteOverrides = settings.PaletteOverrides ?? new Dictionary<int, int>();
        paletteSlot = settings.PaletteSlot;

        engine = new LightingEngine(Current(), files);
        BindActions();
        UpdatePresentation();
        RefreshPreview();
        ResumeLayout(performLayout: true);
        refresh.Start();
    }

    protected override bool ShowWithoutActivation => backgroundPreview;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Caption(Handle);
    }

    private Control BuildLayout()
    {
        var layout = Table(2);
        layout.BackColor = Background;
        layout.Paint += (s, e) =>
        {
            var g = e.Graphics;
            var scale = DeviceDpi / 96f;
            var rect = new RectangleF(.5f, .5f, layout.ClientSize.Width - 1, layout.ClientSize.Height - 1);
            Satin(g, rect, 26 * scale, Color.FromArgb(30, 39, 50), Color.FromArgb(11, 16, 23), Color.FromArgb(32, 44, 56));
            Glow(g, rect, 30 * scale, Color.FromArgb(18, 32, 46));
        };
        layout.Padding = new Padding(24, 15, 24, 24);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = BuildHeader();
        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 2);
        preview.Dock = DockStyle.Fill;
        preview.Margin = new Padding(0, 0, 16, 0);
        preview.Controls.Add(BuildBrightness());
        layout.Controls.Add(preview, 0, 1);

        var side = Table(1);
        side.BackColor = Background;

        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.Controls.Add(BuildSettings(), 0, side.RowCount++);
        layout.Controls.Add(side, 1, 1);

        return layout;
    }

    private Control BuildHeader()
    {
        var header = SingleRow(5);
        header.BackColor = Background;
        header.Padding = new Padding(17, 0, 8, 0);
        header.Paint += (sender, e) =>
        {
            var panel = (Control)sender!;
            DrawDashboardHeader(e.Graphics, panel.ClientSize, panel.DeviceDpi / 96f);
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        var title = Label("Подсветка монитора", 24, color: Color.FromArgb(233, 233, 233),
            font: "Bahnschrift SemiCondensed");
        title.BackColor = Color.Transparent;
        title.Dock = DockStyle.Fill;
        header.Controls.Add(title, 0, 0);

        badge.Margin = new Padding(0, 0, 10, 0);
        badge.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        badge.Height = 50;
        badge.AccessibleName = "Зелёная лампа режима «Один цвет»";
        header.Controls.Add(badge, 1, 0);
        var amber = new ConnectionBadge
        {
            Margin = badge.Margin, Anchor = badge.Anchor, Height = badge.Height,
            AccessibleName = "Жёлтая лампа переключения",
            LitGlass = (Color.FromArgb(255, 231, 139), Color.FromArgb(176, 92, 13), Color.FromArgb(124, 64, 12)),
            UnlitGlass = (Color.FromArgb(85, 52, 22), Color.FromArgb(34, 20, 9), Color.FromArgb(131, 81, 34))
        };
        colorLamp.Margin = badge.Margin;
        colorLamp.Anchor = badge.Anchor;
        colorLamp.Height = badge.Height;
        drive.MotionChanged += (_, _) => amber.UpdateStatus(
            drive.IsMoving ? "Жёлтая лампа горит" : "Жёлтая лампа выключена",
            drive.IsMoving, drive.IsMoving);
        screenLamp.Margin = badge.Margin;
        screenLamp.Anchor = badge.Anchor;
        screenLamp.Height = badge.Height;
        header.Controls.Add(screenLamp, 2, 0);
        header.Controls.Add(amber, 3, 0);
        header.Controls.Add(colorLamp, 4, 0);
        return header;
    }

    private Control BuildSettings()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(18, 15, 18, 14) };
        var content = Table(1);
        AddRow(content, Label("Подсветка", 14, true), 30);
        AddRow(content, powerState, 22);
        AddRow(content, drive, 155);

        screenSettings.BackColor = Field;
        screenSettings.Padding = new Padding(9, 0, 9, 0);

        AddRow(screenSettings, Label("Монитор", 9, true, Muted), 24);
        AddRow(screenSettings, monitor, 118);
        AddRow(screenSettings, reverse, 28);
        content.Paint += (_, e) =>
        {
            var bounds = RectangleF.FromLTRB(.5f, drive.Bottom + 1, content.ClientSize.Width - .5f,
                content.ClientSize.Height - .5f);
            DrawDashboardPlastic(e.Graphics, bounds, content.DeviceDpi / 96f, shadeOpacity: 38);
        };
        var details = new Panel { Margin = new Padding(0, 12, 0, 0), BackColor = Color.Transparent };
        captureStatus.BackColor = Color.Transparent;
        status.BackColor = Color.Transparent;
        details.Controls.Add(screenSettings);
        details.Controls.Add(colors);
        AddRow(content, details, 183);
        AddRow(content, captureStatus, 22);
        AddRow(content, status, 40);
        AddSpace(content);
        card.Controls.Add(content);
        return card;
    }

    private Control BuildBrightness()
    {
        var content = Table(1);
        content.Dock = DockStyle.Bottom;
        content.Height = 53;
        content.BackColor = WindowStyle.Panel;
        var level = SingleRow(2);
        level.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        level.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        var brightnessLabel = Label("Яркость", 10, true);
        brightnessLabel.Dock = DockStyle.Fill;
        level.Controls.Add(brightnessLabel, 0, 0);
        brightnessValue.Dock = DockStyle.Fill;
        brightnessValue.TextAlign = ContentAlignment.MiddleRight;
        level.Controls.Add(brightnessValue, 1, 0);
        AddRow(content, level, 24);
        AddRow(content, brightness, 28);
        return content;
    }

    private void BindActions()
    {
        Shown += (_, _) => ActiveControl = drive;
        drive.ValueChanged += (_, _) =>
        {
            var choosing = drive.ChoosingColor;
            if (!choosing)
                shadeDrawer?.Retract();
            colorLamp.UpdateStatus(choosing ? "Выбор цвета" : "Выбор цвета закрыт", choosing, choosing);
            Changed();
        };
        monitor.SelectionChanged += (_, _) => Changed();
        preview.StripPathChanged += () =>
        {
            reverse.Enabled = preview.StripPath is null;
            Changed();
        };
        reverse.CheckedChanged += (_, _) =>
        {
            preview.ReverseStrip = reverse.Checked;
            Changed();
        };
        brightness.ValueChanged += (_, _) => Changed();
        BuildPalette();
        refresh.Tick += (_, _) => RefreshPreview();
        save.Tick += (_, _) => SaveSettings();
        FormClosed += (_, _) =>
        {
            refresh.Stop();
            if (save.Enabled)
                SaveSettings();
            refresh.Dispose();
            save.Dispose();
            engine.Dispose();
        };
    }

    private void Changed(bool saveSettings = true)
    {
        engine.Update(Current());
        UpdatePresentation();
        if (saveSettings)
        {
            save.Stop();
            save.Start();
        }
    }

    private LightingInput Current()
    {
        var selected = monitor.SelectedDisplay ?? Screen.PrimaryScreen;

        return new LightingInput(new LightSettings
        {
            Port = savedPort,
            Monitor = selected?.DeviceName ?? "",
            Brightness = brightness.Value,
            ColorArgb = color.ToArgb(),
            PaletteOverrides = paletteOverrides.Count == 0 ? null : new Dictionary<int, int>(paletteOverrides),
            PaletteSlot = paletteSlot,
            Mode = drive.Mode,
            StripPath = preview.StripPath,
            Reverse = reverse.Checked
        }, selected?.Bounds ?? Rectangle.Empty, drive.IsOn, drive.IsOn && !backgroundPreview ? "auto" : null);
    }

    private void UpdatePresentation()
    {
        colors.Visible = drive.ChoosingColor;
        screenSettings.Visible = drive.IsOn && drive.Mode == LightMode.Screen;
        brightnessValue.Text = $"{brightness.Value} %";
        powerState.Text = drive.ChoosingColor ? "Выбор цвета" : drive.IsOn ? "Включена" : "Выключена";

    }

    private void RefreshPreview()
    {
        if (++displayRefresh % 10 == 0)
            monitor.RefreshDisplays(monitor.SelectedDisplay?.DeviceName);
        preview.SetFrame(engine.Preview);
        var link = engine.Status;
        var screenMode = drive.Mode == LightMode.Screen;
        var modeStatus = !drive.IsOn ? "Выключено" : !link.Connected ? "Поиск контроллера" : "Подключено";
        badge.UpdateStatus(modeStatus, link.Connected, drive.IsOn && !screenMode);
        screenLamp.UpdateStatus(modeStatus, link.Connected, drive.IsOn && screenMode);
        status.Text = drive.IsOn && !link.Connected ? link.Message : "";
        captureStatus.Text = drive.Mode == LightMode.Screen ? engine.CaptureError ?? "" : "";
    }

    private void BuildPalette()
    {
        var choices = new (string Name, int Rgb)[]
        {
            ("Тёплый белый", 0xFFD6A6), ("Холодный белый", 0xE4EEFF),
            ("Янтарный", 0xFFB85C), ("Красный", 0xFF605B),
            ("Розовый", 0xF38EBE), ("Фиолетовый", 0xAB91FF),
            ("Синий", 0x619DFF), ("Бирюзовый", 0x94DCE4), ("Зелёный", 0x7BDAA3)
        };
        colors.Name = "ColorPalette";
        colors.AccessibleName = "Цвет ленты";
        colors.BackColor = Color.FromArgb(20, 24, 28);
        colors.Padding = new Padding(6);
        for (var column = 0; column < 3; column++)
            colors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (var row = 0; row < 4; row++)
            colors.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        colors.RowCount = 4;

        if (paletteSlot is int savedSlot && (savedSlot < 0 || savedSlot >= choices.Length))
            paletteSlot = null;

        var inferSlot = paletteSlot is null;
        for (var index = 0; index < choices.Length; index++)
        {
            var (name, rgb) = choices[index];
            var slot = index;
            var shade = Color.FromArgb(paletteOverrides.TryGetValue(slot, out var argb)
                ? argb : unchecked((int)0xFF000000) | rgb);
            var button = new StudioButton(name, name) { Tag = slot, Swatch = shade, Dock = DockStyle.Fill, Margin = new Padding(3) };
            if (inferSlot && paletteSlot is null && shade.ToArgb() == color.ToArgb())
                paletteSlot = slot;
            button.Click += (_, _) =>
            {
                shadeDrawer?.Retract();
                paletteSlot = slot;
                SelectColor(button.Swatch ?? shade);
            };
            colors.Controls.Add(button, index % 3, index / 3);
        }

        var custom = new StudioButton("Другой цвет…", "Другой цвет…") { Dock = DockStyle.Fill, Margin = new Padding(3) };
        custom.Click += (_, _) =>
        {
            if (shadeDrawer is not null)
            {
                shadeDrawer.Retract();
                return;
            }

            var drawer = new ShadeDialog(color);
            shadeDrawer = drawer;
            drawer.PreviewChanged += shade =>
            {
                if (drawer.IsRetracting)
                    return;
                var current = Current();
                engine.Update(current with { Settings = current.Settings with { ColorArgb = shade.ToArgb() } });
            };
            drawer.ShadeAccepted += ReplaceSelectedPaletteColor;
            drawer.FormClosed += (_, _) =>
            {
                shadeDrawer = null;
                if (!Disposing && !IsDisposed)
                    engine.Update(Current());
            };
            drawer.ShowBeside(this, colors);
        };
        customColor = custom;
        colors.Controls.Add(custom, 0, 3);
        colors.SetColumnSpan(custom, 3);
        HighlightColor();
    }

    private void SelectColor(Color shade)
    {
        color = shade;
        colorLamp.UpdateStatus("Цвет выбран", false, false);
        HighlightColor();
        Changed();
    }

    internal void ReplaceSelectedPaletteColor(Color shade)
    {
        if (paletteSlot is not int slot)
            return;

        var button = colors.Controls.OfType<StudioButton>().Single(item => item.Tag is int index && index == slot);
        paletteOverrides[slot] = shade.ToArgb();
        button.Swatch = shade;
        SelectColor(shade);
    }

    private void HighlightColor()
    {
        if (customColor is not null)
            customColor.Enabled = paletteSlot.HasValue;

        foreach (var button in colors.Controls.OfType<StudioButton>())
        {
            button.Primary = button.Tag is int slot && slot == paletteSlot;
            button.Invalidate();
        }
    }

    private void SaveSettings()
    {
        save.Stop();
        try { files.Save(Current().Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files.Log("Не удалось сохранить настройки. " + ex.Message);
            status.Text = "Не удалось сохранить настройки.";
        }
    }

    private static TableLayoutPanel Table(int columns)
    {
        var table = new TableLayoutPanel { ColumnCount = columns, Dock = DockStyle.Fill, Margin = Padding.Empty };
        if (columns == 1)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, Control control, int height)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 0, table.RowCount++);
    }

    private static TableLayoutPanel SingleRow(int columns)
    {
        var table = Table(columns);
        AddSpace(table);
        return table;
    }

    private static void AddSpace(TableLayoutPanel table)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowCount++;
    }
}
