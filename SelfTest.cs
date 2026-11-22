using Microsoft.Win32;

namespace Kontur;

internal static class SelfTest
{
    public static int Run(string result)
    {
        try
        {
            var pattern = Enumerable.Repeat(new Rgb(13, 36, 7), 65).ToArray();
            var fromDeviceLog = Convert.FromHexString("416461000041" + string.Concat(Enumerable.Repeat("0d2407", 65)));
            Check(Sk0127.Encode(pattern).SequenceEqual(fromDeviceLog), "Формат кадра из журнала SK0127");
            var off = Sk0127.Encode(new Rgb[65]);
            Check(off.AsSpan(6).IndexOfAnyExcept((byte)0) < 0, "Выключение передаёт чёрный на все светодиоды");

            pattern[0] = new Rgb(255, 0, 0);
            pattern[^1] = new Rgb(0, 0, 255);
            Check(Sk0127.Encode(pattern, true).AsSpan(6, 3).SequenceEqual(new byte[] { 0, 0, 255 }), "Разворот ленты");
            Check(Sk0127.Cells.Length == 65 && Sk0127.Cells[0] == new Point(0, 16) &&
                Sk0127.Cells[17] == new Point(0, 0) && Sk0127.Cells[47] == new Point(30, 0) &&
                Sk0127.Cells[64] == new Point(30, 16), "Раскладка 17 / 31 / 17");
            var reply = Convert.FromHexString("00534b303132372c00010d0a0405060d0a");
            Check(Sk0127.Identified(reply) && !Sk0127.Identified(reply.AsSpan(0, reply.Length - 1)), "Ответ с переводом строки в серийных байтах");
            Check(Sk0127.Correct(new Rgb(255, 255, 255), 0) == default &&
                Sk0127.Correct(new Rgb(255, 128, 0), 100) == new Rgb(255, 55, 0), "Яркость и гамма");
            Check(SerialLink.IsPort("COM12") && !SerialLink.IsPort("COM0") &&
                !SerialLink.IsPort(@"COM1\test"), "Валидация порта");

            TestPortSelection();
            TestHazardFrame();
            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result))!, "config");
            var files = new LocalFiles(folder);
            var settings = new LightSettings { Brightness = 26, Reverse = true, Mode = LightMode.Screen };
            files.Save(settings);
            Check(files.Load() == settings, "Сохранение и загрузка настроек");
            files.Log("Проверка записи в журнал");
            Check(File.ReadAllText(files.LogFile).Contains("Проверка записи"), "Диагностический журнал");
            TestScreenRecovery(new LocalFiles(Path.Combine(folder, "capture-recovery")));
            File.WriteAllText(result, "OK · протокол SK0127, выбор COM-порта, потеря и восстановление экрана, яркость, настройки и журнал");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(result, error.ToString());
            return 1;
        }
    }

    private static void TestHazardFrame()
    {
        var settings = new LightSettings { Brightness = 46 };
        var amber = Sk0127.Correct(new Rgb(255, 177, 52), settings.Brightness);
        var lit = LightingEngine.HazardFrame(settings, .80).AsSpan();
        Check(lit[..Sk0127.Rows].IndexOfAnyExcept(amber) < 0 &&
            lit.Slice(Sk0127.Rows, Sk0127.Columns).IndexOfAnyExcept(default(Rgb)) < 0 &&
            lit[^Sk0127.Rows..].IndexOfAnyExcept(amber) < 0, "Аварийка зажигает обе боковины, верх остаётся тёмным");
        byte RedAt(double seconds) => LightingEngine.HazardFrame(settings, seconds)[0].Red;
        Check(RedAt(0) == 0 && RedAt(.18) > 0 && RedAt(.44) > RedAt(.18) &&
            RedAt(.44) < amber.Red && RedAt(.80) == amber.Red,
            "Аварийка плавно загорается и держит пик");
        Check(RedAt(1.16) < amber.Red && RedAt(1.46) > RedAt(1.76) &&
            RedAt(1.76) > RedAt(2.08) && RedAt(2.08) > 0 &&
            LightingEngine.HazardFrame(settings, 2.45).AsSpan().IndexOfAnyExcept(default(Rgb)) < 0 &&
            LightingEngine.HazardFrame(settings, 3.56).AsSpan().SequenceEqual(lit),
            "Обе боковины плавно гаснут вместе, пауза и цикл повторяются");
        var path = Enumerable.Repeat(new PointF(.5f, .9f), Sk0127.Count).ToArray();
        path[0] = new PointF(.93f, .27f);
        path[^1] = new PointF(.07f, .73f);
        var custom = LightingEngine.HazardFrame(settings with { StripPath = path }, .80);
        Check(custom[0] == amber && custom[^1] == amber &&
            custom.AsSpan(1, custom.Length - 2).IndexOfAnyExcept(default(Rgb)) < 0,
            "Аварийка использует боковые светодиоды нарисованного маршрута");
        Check(LedPreview.AlignedHazardFrame(custom, path).AsSpan().SequenceEqual(lit),
            "Ореолы в предпросмотре выровнены при несимметричном маршруте");
        custom[0] = default;
        Check(LedPreview.AlignedHazardFrame(custom, path).AsSpan()
            .SequenceEqual(Sk0127.SideFrame(default, amber)),
            "Предпросмотр не подсвечивает отсутствующую боковину");
    }

    private static void TestPortSelection()
    {
        Check(SerialLink.SelectControllerPort(SerialLink.Automatic, ["COM12"]) == "COM12", "Выбор единственного CH340");
        Check(SerialLink.SelectControllerPort("com7", ["COM7", "COM12"]) == "COM7", "Ручной выбор CH340");
        ExpectUnavailable(SerialLink.Automatic, ["COM7", "COM12"], "несколько");
        ExpectUnavailable(SerialLink.Automatic, [], "не найден");
        ExpectUnavailable("COM7", ["COM12"], "COM7");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<LightSettings>("""{"Port":"COM12","Brightness":32}""");
        Check(legacy is { PreferredPort: null, Brightness: 32 }, "Старый COM-порт не превращается в ручной выбор");

        static void ExpectUnavailable(string selection, string[] candidates, string message)
        {
            try { SerialLink.SelectControllerPort(selection, candidates); }
            catch (IOException ex)
            {
                Check(ex.Message.Contains(message, StringComparison.Ordinal), "Понятная причина отказа COM-подключения");
                return;
            }
            throw new InvalidOperationException("Нет подходящего COM-порта.");
        }
    }

    private static void TestScreenRecovery(LocalFiles files)
    {
        var screen = new Rectangle(0, 0, 640, 480);
        var otherScreen = new Rectangle(640, 0, 640, 480);
        var settings = new LightSettings { ColorArgb = Color.Magenta.ToArgb(), Brightness = 100, Mode = LightMode.Screen };
        var available = 0;
        var input = new LightingInput(settings, screen, true, null);
        var red = new Rgb(255, 0, 0);
        var blue = new Rgb(0, 0, 255);
        using var engine = new LightingEngine(input, files, (display, _) =>
        {
            if (Volatile.Read(ref available) == 0)
                throw new InvalidOperationException("Monitor unavailable");
            return Enumerable.Repeat(display == screen ? red : blue, Sk0127.Count).ToArray();
        });
        static bool Wait(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(3));
        Check(Wait(() => engine.CaptureError is not null), "Виден сбой захвата");
        Check(engine.Preview.All(pixel => pixel == default), "Нет однотонной засветки при старте без кадра");

        Volatile.Write(ref available, 1);
        Check(Wait(() => engine.CaptureError is null && engine.Preview[0] == red), "Восстановление захвата");
        Volatile.Write(ref available, 0);
        Check(Wait(() => engine.Preview[0].Red is > 0 and < 255), "Подсветка гаснет плавно при потере кадра");
        Check(Wait(() => engine.CaptureError is not null && engine.Preview.All(pixel => pixel == default)), "Устаревший кадр не зависает");
        Volatile.Write(ref available, 1);
        engine.Update(input with { Display = otherScreen });
        Check(Wait(() => engine.CaptureError is null && engine.Preview[0] == blue), "Кадр берётся с нового монитора");
        Check(!engine.Status.Connected, "Тест не открывает USB");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
    }

    public static int CheckScreen(string result)
    {
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Windows не видит дисплей.");
            var files = new LocalFiles(Path.GetDirectoryName(Path.GetFullPath(result))!);
            var settings = new LightSettings { Brightness = 100, ColorArgb = Color.Black.ToArgb(), Mode = LightMode.Screen };
            using var engine = new LightingEngine(new LightingInput(settings, bounds, true, null), files);
            var captured = SpinWait.SpinUntil(() => engine.Preview.Any(pixel => pixel != default), TimeSpan.FromSeconds(4));
            Check(captured, engine.CaptureError ?? "Захват дисплея не вернул ни одной освещённой зоны.");
            Check(!engine.Status.Connected && engine.Preview.Length == 65, "Захват работает без подключения COM-порта");
            engine.Update(new LightingInput(settings, bounds, false, null));
            Check(SpinWait.SpinUntil(() => engine.Preview.All(pixel => pixel == default), TimeSpan.FromSeconds(1)),
                "Выключение в режиме захвата");
            File.WriteAllText(result, "OK · захват рабочего стола, 65 зон, предпросмотр без USB, выключение");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(result, error.ToString());
            return 1;
        }
    }

    private static void TestMusicSpectrum()
    {
        const int sampleRate = 48000;
        var spectrum = new AudioSpectrum();
        Check(spectrum.Measure(sampleRate) == AudioBands.Silence, "Нет аудиоданных при запуске");
        Rgb Tone(int frequency)
        {
            for (var i = 0; i < AudioSpectrum.WindowSize; i++)
                spectrum.Add((float)(.9 * Math.Sin(2 * Math.PI * frequency * i / sampleRate)));
            return LightingEngine.MusicPixel(spectrum.Measure(sampleRate), .5, 47);
        }

        var bass = Tone(110);
        var mid = Tone(800);
        var treble = Tone(4800);
        Check(bass.Red > 10 && bass.Green == 0 && bass.Blue == 0 &&
            mid.Green > 10 && mid.Red == 0 && mid.Blue == 0 &&
            treble.Blue > 10 && treble.Red == 0 && treble.Green == 0,
            "Частоты одинаковой и высокой громкости дают разные цвета");
        var quiet = new AudioBands(.2, .2, .2);
        var loud = new AudioBands(.65, .2, .2);
        var calmPixel = LightingEngine.MusicPixel(quiet.Accentuate(quiet), 0, 47);
        var accentPixel = LightingEngine.MusicPixel(loud.Accentuate(quiet), 0, 47);
        var heldPixel = LightingEngine.MusicPixel(loud.Accentuate(loud), 0, 47);
        Check(accentPixel.Red >= calmPixel.Red * 5 && accentPixel.Red > heldPixel.Red,
            "Акцент ярче тихого участка и удерживаемой ноты");
        var mixed = LightingEngine.MusicPixel(new AudioBands(.64, .39, .18), .25, 47);
        Check(mixed is { Red: > 20, Green: > 5, Blue: 0 }, "Смешанный цвет сохраняет насыщенность");
        for (var i = 0; i < AudioSpectrum.WindowSize; i++)
            spectrum.Add(0);
        Check(spectrum.Measure(sampleRate) == AudioBands.Silence, "В тишине подсветка гаснет");
        Check(LightingEngine.MusicPixel(new AudioBands(1, 1, 1), .5, 0) == default,
            "Цветомузыка учитывает яркость");
    }

    public static int CheckWindow(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = Path.Combine(folder, "result.txt");
        var startupKey = @"Software\KonturSelfTest-" + Guid.NewGuid().ToString("N");
        var startup = new WindowsStartup(startupKey);
        try
        {
            TestMusicSpectrum();
            Check(DriveSelector.AlongGate(PointF.Empty, new PointF(1, 1), .25f) == new PointF(0, .5f),
                "Рычаг выходит из верхнего положения по вертикальному пазу");
            Check(DriveSelector.AlongGate(new PointF(-1, 1), new PointF(1, 1), .5f) == new PointF(0, 1),
                "Рычаг проходит между нижними положениями по горизонтальному пазу");
            Check(DriveSelector.AlongGate(new PointF(.5f, 1), PointF.Empty, .5f) == new PointF(0, .75f),
                "При смене цели рычаг продолжает движение из текущей точки");
            TestAmbientWall();
            if (Screen.AllScreens.Length > 0)
            {
                using var disconnected = new MonitorSelector();
                disconnected.RestoreDisplay("kontur-test-disconnected-display");
                Check(disconnected.SelectedDisplay is null && disconnected.PreferredDisplay == "kontur-test-disconnected-display",
                    "Отключённый монитор сохраняется до появления, основной экран не подставляется");
            }
            var files = new LocalFiles(Path.Combine(folder, "config"));
            files.Save(new LightSettings { Brightness = 51, ColorArgb = Color.FromArgb(134, 218, 227).ToArgb() });
            using var window = new MainWindow(files, backgroundPreview: true, controllerPorts: () => ["COM7", "COM12"],
                startup: startup, startInHazard: true)
            {
                ShowInTaskbar = false,
                Opacity = 0
            };
            window.Show();
            PumpWindow(1350);

            var controls = Descendants(window).ToArray();
            var toggle = controls.OfType<StartupSwitch>().Single();
            Check(!toggle.Checked && !startup.IsEnabled(), "Автозагрузка выключена без регистрации");
            var startupDrive = controls.OfType<DriveSelector>().Single();
            Check(startupDrive.IsNeutral && controls.OfType<Label>().Any(item => item.Text == "Аварийка"),
                "Запуск по команде Windows открывает приложение на N с аварийкой");
            startupDrive.AccessibilityObject.Value = "Выключено";
            PumpWindow(900);
            toggle.Checked = true;
            using (var run = Registry.CurrentUser.OpenSubKey(startupKey))
            {
                Check(run?.GetValue(WindowsStartup.EntryName) is string command &&
                    command == $"\"{Application.ExecutablePath}\" {WindowsStartup.LaunchArgument}",
                    "Автозагрузка записывает путь и аргумент запуска аварийки");
            }
            Check(new WindowsStartup(startupKey).IsEnabled(), "Автозагрузка сохраняется между экземплярами");
            using (var run = Registry.CurrentUser.OpenSubKey(startupKey, writable: true)!)
                run.SetValue(WindowsStartup.EntryName, $"\"{Application.ExecutablePath}\"");
            startup.UpgradeLegacyCommand();
            using (var run = Registry.CurrentUser.OpenSubKey(startupKey))
                Check(run?.GetValue(WindowsStartup.EntryName) is string upgraded &&
                    upgraded == $"\"{Application.ExecutablePath}\" {WindowsStartup.LaunchArgument}",
                    "Обновление сохраняет прежнюю автозагрузку и добавляет аргумент");
            CaptureWindow(window, folder, "startup-on.png");
            toggle.Checked = false;
            Check(!startup.IsEnabled(), "Переключатель удаляет запись автозагрузки");
            var portPicker = controls.OfType<ControllerPicker>().Single();
            Check(portPicker.Selection?.Caption == "Авто" && portPicker.Options.Count == 3,
                "Перечень USB-контроллеров начинается с режима «Авто»");
            Check(portPicker.Visible && portPicker.Width >= 160 && portPicker.Height >= 43, $"Поле USB-контроллера должно быть видно. Размер {portPicker.Bounds}.");
            portPicker.PerformClick();
            PumpWindow();
            Check(portPicker.IsOpen, "Список доступных COM-портов открывается");
            portPicker.Choose(portPicker.Options[2]);
            Check(!portPicker.IsOpen, "Список закрывается после выбора");
            PumpWindow(450);
            Check(files.Load().PreferredPort == "COM12", "Выбранный COM-порт сохраняется");
            portPicker.Choose(portPicker.Options[0]);
            PumpWindow(450);
            Check(files.Load().PreferredPort is null, "Возврат в режим «Авто» сохраняется");
            var slider = controls.OfType<BrightnessSlider>().Single();
            var drive = controls.OfType<DriveSelector>().Single();
            var monitorChoice = controls.OfType<StudioButton>().Single(item => item.AccessibleName == "Выбор монитора");
            monitorChoice.PerformClick();
            PumpWindow();
            var monitor = window.OwnedForms.SelectMany(Descendants).OfType<MonitorSelector>().Single();
            Check(monitor.Visible, "Мониторы доступны при выключенной подсветке");
            CaptureWindow(monitor.FindForm()!, folder, "monitor-panel.png");
            monitorChoice.PerformClick();
            var preview = controls.OfType<LedPreview>().Single();
            var palette = controls.OfType<TableLayoutPanel>().Single(item => item.Name == "ColorPalette");
            var displayChoices = Descendants(monitor).OfType<CheckBox>().ToArray();
            Check(slider.Value == 51, "Загрузка прежней схемы настроек");
            Check(slider.FindForm() == window && preview.Contains(slider), "Регулятор яркости находится под предпросмотром");
            drive.AccessibilityObject.Value = "Один цвет";
            drive.AccessibilityObject.Value = "Выбор цвета";
            var viewport = palette.Parent!;
            var fullHeight = palette.Height;
            Check(PumpUntil(() => viewport.Height > fullHeight * .36 && viewport.Height < fullHeight * .88, 500)
                && palette.Bottom == viewport.Height,
                $"Палитра плавно выезжает, сохраняя высоту: {viewport.Height} из {fullHeight}");
            drive.AccessibilityObject.Value = "Один цвет";
            Check(viewport.Visible && !portPicker.Visible && !toggle.Visible,
                "Быстрый возврат разворачивает движение без скрытия палитры");
            var heightAtReversal = viewport.Height;
            Check(PumpUntil(() => viewport.Height > 0 && viewport.Height < heightAtReversal, 230)
                && palette.Height == fullHeight,
                $"Палитра задвигается от текущего положения: {heightAtReversal} → {viewport.Height}");
            drive.AccessibilityObject.Value = "Выбор цвета";
            Check(viewport.Height > 0 && viewport.Height < fullHeight,
                "Повторный выезд начинается с текущего положения");
            PumpWindow(360);
            Check(drive.ChoosingColor && drive.IsOn && palette.Visible &&
                palette.Controls.OfType<StudioButton>().Any(item => item.Text == "Другой цвет…"), "Палитра на левой ветке");
            CheckPaletteLayout(palette, drive, portPicker, toggle);
            palette.Controls.OfType<StudioButton>().Single(item => item.Text == "Синий").PerformClick();
            PumpWindow(450);
            Check(files.Load().ColorArgb == Color.FromArgb(97, 157, 255).ToArgb() && palette.Visible,
                "Цвет применяется сразу, палитра остаётся открытой");
            palette.Controls.OfType<StudioButton>().Single(item => item.Text == "Красный").PerformClick();
            PumpWindow(450);
            Check(files.Load().ColorArgb == Color.FromArgb(255, 96, 91).ToArgb() && palette.Visible,
                "Следующий оттенок применяется без закрытия палитры");
            var editedShade = Color.FromArgb(221, 72, 41);
            window.ReplaceSelectedPaletteColor(editedShade);
            PumpWindow(450);
            var red = palette.Controls.OfType<StudioButton>().Single(item => item.Text == "Красный");
            var blue = palette.Controls.OfType<StudioButton>().Single(item => item.Text == "Синий");
            Check(red.Swatch == editedShade && blue.Swatch == Color.FromArgb(255, 97, 157, 255),
                "Другой цвет заменяет только выбранную кнопку");
            Check(files.Load().PaletteOverrides?[3] == editedShade.ToArgb() && files.Load().PaletteSlot == 3 &&
                files.Load().ColorArgb == editedShade.ToArgb(), "Оттенок кнопки и выбор сохраняются");
            blue.PerformClick();
            red.PerformClick();
            PumpWindow(450);
            Check(files.Load().ColorArgb == editedShade.ToArgb(), "Повторное нажатие применяет отредактированный оттенок");
            CaptureWindow(window, folder, "palette.png");
            drive.AccessibilityObject.Value = "Один цвет";
            PumpWindow(100);
            Check(viewport.Visible && viewport.Height < palette.Height && !portPicker.Visible && !toggle.Visible,
                "Нижние настройки ждут окончания плавного закрытия");
            PumpWindow(240);
            Check(!palette.Visible && files.Load().ColorArgb == editedShade.ToArgb(), "Выход сохраняет оттенок");
            Check(portPicker.Visible && portPicker.CanSelect && toggle.Visible && toggle.CanSelect,
                "После закрытия палитры USB-контроллер и автозагрузка снова доступны");
            Check(!monitor.Visible && preview.Visible, "Настройки режима «Один цвет»");
            CheckLayout(controls);
            CaptureWindow(window, folder, "solid.png");
            var installation = controls.OfType<StudioButton>().Single(item => item.AccessibleName == "Настройка положения ленты");
            installation.PerformClick();
            PumpWindow(430);
            Check(installation.Text == "Вид спереди", "Переключение на схему установки");
            var startDrawing = controls.OfType<StudioButton>().Single(item => item.AccessibleName == "Начать рисовать");
            Check(startDrawing.Visible, "Перед рисованием видна подсказка");
            CaptureWindow(window, folder, "installation-hint.png");
            startDrawing.PerformClick();
            Check(!startDrawing.Visible, "Кнопка открывает холст");
            CaptureWindow(window, folder, "installation.png");
            var clearRoute = controls.OfType<StudioButton>().Single(item => item.AccessibleName == "Очистить маршрут");
            Check(clearRoute.Visible && clearRoute.Enabled, "Кнопка очистки доступна на холсте");
            clearRoute.PerformClick();
            CaptureWindow(window, folder, "installation-cleared.png");
            installation.PerformClick();
            PumpWindow(430);
            Check(installation.Text == "Настройка положения ленты", "Возврат к виду спереди");

            var beforeBrightness = slider.DisplayedValue;
            slider.AccessibilityObject.Value = "100";
            if (SystemInformation.UIEffectsEnabled)
            {
                Check(slider.DisplayedValue == beforeBrightness && slider.Value == 100,
                    "Анимация регулятора начинается с текущего положения");
                PumpWindow(60);
                Check(slider.DisplayedValue > beforeBrightness && slider.DisplayedValue < 100,
                    "Бегунок проходит промежуточное положение");
            }
            PumpWindow();
            Check(slider.Value == 100 && slider.DisplayedValue == 100, "Регулятор доходит до полной яркости");
            CaptureWindow(window, folder, "solid-max.png");
            foreach (var (name, mode) in new[] { ("Музыка", LightMode.Music),
                ("Волшебный", LightMode.Magic), ("Бегущие огни", LightMode.Chase) })
            {
                drive.AccessibilityObject.Value = name;
                PumpWindow(450);
                Check(drive.IsOn && drive.Mode == mode && drive.AccessibilityObject.Value == name, name);
                Check(files.Load().Mode == mode, "Сохранение режима " + name);
            }
            CaptureWindow(window, folder, "effects.png");
            slider.AccessibilityObject.Value = "46";
            drive.AccessibilityObject.Value = "Нейтраль N";
            PumpWindow(850);
            var amber = Sk0127.Correct(new Rgb(255, 177, 52), 46);
            Check(drive.IsNeutral && drive.Mode == LightMode.Chase &&
                files.Load() is { Mode: LightMode.Chase, Brightness: 46 } neutralSettings &&
                neutralSettings.ColorArgb == editedShade.ToArgb(), "Нейтраль сохраняет предыдущий режим и оттенок");
            var sawPeak = false;
            var sawGradient = false;
            var sawPause = false;
            for (var i = 0; i < 64 && (!sawPeak || !sawGradient || !sawPause); i++)
            {
                PumpWindow(50);
                var frame = preview.Frame;
                if (frame.IndexOfAnyExcept(default(Rgb)) < 0)
                    sawPause = true;
                else
                {
                    Check(frame[0] == frame[^1] && frame[0].Red <= amber.Red &&
                        frame[Sk0127.Rows] == default, "На N яркость меняется одновременно на обеих боковинах");
                    sawGradient |= frame[0].Red > 0 && frame[0].Red < amber.Red;
                    if (frame[0] == amber && !sawPeak)
                    {
                        CaptureWindow(window, folder, "neutral.png");
                        sawPeak = true;
                    }
                }
            }
            Check(sawPeak && sawGradient && sawPause && controls.OfType<Label>().Any(item => item.Text == "Аварийка"),
                "Аварийка в предпросмотре проходит пик, промежуточную яркость и паузу");

            drive.AccessibilityObject.Value = "Цвета экрана";
            slider.AccessibilityObject.Value = "64";
            PumpWindow();
            monitorChoice.PerformClick();
            PumpWindow();
            Check(monitor.Visible && slider.Value == 64 && displayChoices.Length == Screen.AllScreens.Length,
                "Схема показывает все подключённые дисплеи");
            if (displayChoices.Length > 1)
            {
                var nextDisplay = displayChoices.First(item => !item.Checked);
                nextDisplay.AccessibilityObject.DoDefaultAction();
                PumpWindow();
                Check(nextDisplay.Checked && displayChoices.Count(item => item.Checked) == 1,
                    "Щелчок выбирает один монитор");
            }
            monitorChoice.PerformClick();
            window.Size = window.MinimumSize;
            PumpWindow();
            CheckLayout(controls);
            CaptureWindow(window, folder, "screen-min.png");
            drive.AccessibilityObject.Value = "Выбор цвета";
            Check(drive.Mode == LightMode.Screen && !palette.Visible, "Правая ветка не открывает палитру");
            drive.AccessibilityObject.Value = "Один цвет";
            drive.AccessibilityObject.Value = "Выбор цвета";
            PumpWindow(360);
            CheckPaletteLayout(palette, drive, portPicker, toggle);
            CheckLayout(controls);
            CaptureWindow(window, folder, "palette-min.png");
            drive.AccessibilityObject.Value = "Цвета экрана";
            PumpWindow(320);
            Check(!palette.Visible && portPicker.Visible && toggle.Visible,
                "В минимальном окне настройки возвращаются после закрытия палитры");

            drive.AccessibilityObject.Value = "Выключено";
            PumpWindow(1000);
            Check(!drive.IsOn && drive.Mode == LightMode.Screen &&
                preview.Frame.IndexOfAnyExcept(default(Rgb)) < 0, "Положение «Выкл» гасит свет и сохраняет режим");
            CaptureWindow(window, folder, "off.png");
            drive.AccessibilityObject.Value = "Один цвет";
            Check(drive.IsOn && drive.Mode == LightMode.Solid, "Включение однотонного режима");
            drive.AccessibilityObject.Value = "Цвета экрана";
            Check(drive.IsOn && drive.Mode == LightMode.Screen, "Переход между режимами");
            Check(controls.OfType<Label>().Any(item => item.Text.Contains("Выключено", StringComparison.Ordinal) || item.Text.Contains("Поиск", StringComparison.Ordinal)),
                "Проверка интерфейса не подключается к ленте");
            window.Close();
            Check(files.Load().Brightness == 64 && files.Load().Mode == LightMode.Screen
                && files.Load().ColorArgb == editedShade.ToArgb()
                && files.Load().Monitor == monitor.SelectedDisplay?.DeviceName, "Сохранение яркости, режима, оттенка и монитора");
            File.WriteAllText(report, "OK · автозагрузка, палитра, мониторы, COM-порты, режимы, настройки, яркость и минимальный размер");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(report, error.ToString());
            return 1;
        }
        finally { Registry.CurrentUser.DeleteSubKey(startupKey, throwOnMissingSubKey: false); }
    }

    private static void TestAmbientWall()
    {
        using var glow = new AmbientWall();
        using var canvas = new Bitmap(220, 140);
        using var surface = Graphics.FromImage(canvas);
        var stage = new RectangleF(0, 0, 220, 140);
        var rail = new RectangleF(37, 34, 146, 85);
        var paleBlue = new Rgb(40, 180, 255);
        var atHalf = Sample(Sk0127.Correct(paleBlue, 45));
        var atFull = Sample(Sk0127.Correct(paleBlue, 90));
        var red = Sample(Sk0127.Correct(new Rgb(255, 50, 10), 70));
        Check(atHalf.B > 20 && atHalf.B > atHalf.R && atFull.B > atHalf.B * 1.75, "Цвет и яркость рассеянной подсветки");
        Check(red.R > red.B + 60, "Смена оттенка рассеянной подсветки");
        Check(Sample(default).ToArgb() == Color.Black.ToArgb(), "Нулевая яркость без ореола");

        Color Sample(Rgb value)
        {
            surface.Clear(Color.Black);
            glow.Draw(surface, stage, rail, Enumerable.Repeat(value, Sk0127.Count).ToArray());
            return canvas.GetPixel(110, 15);
        }
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    private static void CheckPaletteLayout(Control palette, Control drive, Control portPicker, Control startup)
    {
        var driveBottom = drive.PointToScreen(new Point(0, drive.Height)).Y;
        var paletteTop = palette.PointToScreen(Point.Empty).Y;
        var paletteBottom = paletteTop + palette.Height;
        var viewport = palette.Parent!;
        Check(palette.Visible && viewport.Parent!.Controls.GetChildIndex(viewport) == 0 &&
            palette.Height == viewport.Height && paletteTop >= driveBottom &&
            paletteBottom > startup.Parent!.PointToScreen(startup.Location).Y + startup.Height,
            "Палитра целиком разворачивается вниз поверх настроек");
        Check(palette.Controls.OfType<StudioButton>().All(item => item.Height * 96 >= 34 * item.DeviceDpi),
            "Кнопки палитры сохраняют высоту при изменении размера окна");
        Check(!portPicker.Visible && !portPicker.CanSelect && !startup.Visible && !startup.CanSelect,
            "Перекрытые настройки недоступны, пока палитра открыта");
    }

    private static void CheckLayout(IEnumerable<Control> controls)
    {
        var status = controls.OfType<Label>().Single(item => item.Name == "ConnectionStatus");
        var panel = status.Parent!;
        var startup = controls.OfType<StartupSwitch>().Single();
        Check(status.TextAlign == ContentAlignment.BottomRight && status.Top >= startup.Bottom
            && panel.ClientSize.Height - status.Bottom == status.Margin.Bottom
            && panel.ClientSize.Width - status.Right == status.Margin.Right,
            $"Статус подключения в углу: {status.Bounds}, контейнер {panel.ClientSize}, отступ {status.Margin}, автозагрузка {startup.Bounds}");

        foreach (var control in controls.Where(item => item.Visible && item.Parent is not null))
        {
            var parent = control.Parent!.ClientRectangle;
            parent.Inflate(1, 1);
            Check(parent.Contains(control.Bounds), $"Элемент выходит за границы окна: {control.GetType().Name} {control.Text} {control.Bounds}, контейнер {parent}");
        }
    }

    private static bool PumpUntil(Func<bool> condition, int timeout)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (condition())
                return true;
            Thread.Sleep(10);
        } while (elapsed.ElapsedMilliseconds < timeout);
        return false;
    }

    private static void PumpWindow(int duration = 240)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < duration)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void CaptureWindow(Form window, string directory, string filename)
    {
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        // DrawToBitmap reverses the sibling order, so print the visible overlay last.
        if (window.Controls.Find("ColorPalette", searchAllChildren: true).FirstOrDefault()?.Parent is { Visible: true } viewport)
            viewport.DrawToBitmap(bitmap, new Rectangle(viewport.PointToScreen(Point.Empty) - (Size)window.Location, viewport.Size));
        var start = window.PointToScreen(Point.Empty) - (Size)window.Location;
        using var client = bitmap.Clone(new Rectangle(start, window.ClientSize), bitmap.PixelFormat);
        client.Save(Path.Combine(directory, filename), System.Drawing.Imaging.ImageFormat.Png);
    }
}

