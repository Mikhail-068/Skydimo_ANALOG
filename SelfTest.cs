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

            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result))!, "config");
            var files = new LocalFiles(folder);
            var settings = new LightSettings { Brightness = 26, Reverse = true, Mode = LightMode.Screen };
            files.Save(settings);
            Check(files.Load() == settings, "Сохранение и загрузка настроек");
            files.Log("Проверка записи в журнал");
            Check(File.ReadAllText(files.LogFile).Contains("Проверка записи"), "Диагностический журнал");
            File.WriteAllText(result, "OK · кадр SK0127, выключение, раскладка, ответ контроллера, яркость, настройки и журнал");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(result, error.ToString());
            return 1;
        }
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

    public static int CheckWindow(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = Path.Combine(folder, "result.txt");
        try
        {
            Check(DriveSelector.AlongGate(PointF.Empty, new PointF(1, 1), .25f) == new PointF(0, .5f),
                "Рычаг выходит из верхнего положения по вертикальному пазу");
            Check(DriveSelector.AlongGate(new PointF(-1, 1), new PointF(1, 1), .5f) == new PointF(0, 1),
                "Рычаг проходит между нижними положениями по горизонтальному пазу");
            Check(DriveSelector.AlongGate(new PointF(.5f, 1), PointF.Empty, .5f) == new PointF(0, .75f),
                "При смене цели рычаг продолжает движение из текущей точки");
            TestAmbientWall();
            var files = new LocalFiles(Path.Combine(folder, "config"));
            files.Save(new LightSettings { Brightness = 51, ColorArgb = Color.FromArgb(134, 218, 227).ToArgb() });
            using var window = new MainWindow(files, backgroundPreview: true) { ShowInTaskbar = false, Opacity = 0 };
            window.Show();
            PumpWindow(1350);

            var controls = Descendants(window).ToArray();
            var slider = controls.OfType<BrightnessSlider>().Single();
            var drive = controls.OfType<DriveSelector>().Single();
            var monitor = controls.OfType<MonitorSelector>().Single();
            var preview = controls.OfType<LedPreview>().Single();
            var palette = controls.OfType<TableLayoutPanel>().Single(item => item.Name == "ColorPalette");
            var displayChoices = Descendants(monitor).OfType<CheckBox>().ToArray();
            Check(slider.Value == 51, "Загрузка прежней схемы настроек");
            Check(slider.FindForm() == window && preview.Contains(slider), "Регулятор яркости находится под предпросмотром");
            drive.AccessibilityObject.Value = "Один цвет";
            drive.AccessibilityObject.Value = "Выбор цвета";
            PumpWindow();
            Check(drive.ChoosingColor && drive.IsOn && palette.Visible &&
                palette.Controls.OfType<StudioButton>().Any(item => item.Text == "Другой цвет…"), "Палитра на левой ветке");
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
            PumpWindow();
            Check(!palette.Visible && files.Load().ColorArgb == editedShade.ToArgb(), "Выход сохраняет оттенок");
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

            slider.AccessibilityObject.Value = "100";
            PumpWindow();
            Check(slider.Value == 100, "Регулятор даёт полную яркость");
            CaptureWindow(window, folder, "solid-max.png");

            drive.AccessibilityObject.Value = "Цвета экрана";
            slider.AccessibilityObject.Value = "64";
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
            window.Size = window.MinimumSize;
            PumpWindow();
            CheckLayout(controls);
            CaptureWindow(window, folder, "screen-min.png");
            drive.AccessibilityObject.Value = "Выбор цвета";
            Check(drive.Mode == LightMode.Screen && !palette.Visible, "Правая ветка не открывает палитру");

            drive.AccessibilityObject.Value = "Выключено";
            PumpWindow(1000);
            Check(!drive.IsOn && drive.Mode == LightMode.Screen, "Выключение сохраняет режим");
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
            File.WriteAllText(report, "OK · палитра, мониторы, оба режима, сохранение, яркость, выключатель, минимальный размер");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(report, error.ToString());
            return 1;
        }
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

    private static void CheckLayout(IEnumerable<Control> controls)
    {
        foreach (var control in controls.Where(item => item.Visible && item.Parent is not null))
        {
            var parent = control.Parent!.ClientRectangle;
            parent.Inflate(1, 1);
            Check(parent.Contains(control.Bounds), $"Элемент выходит за границы окна: {control.GetType().Name} {control.Text} {control.Bounds}, контейнер {parent}");
        }
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

    private static void CaptureWindow(MainWindow window, string directory, string filename)
    {
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var start = window.PointToScreen(Point.Empty) - (Size)window.Location;
        using var client = bitmap.Clone(new Rectangle(start, window.ClientSize), bitmap.PixelFormat);
        client.Save(Path.Combine(directory, filename), System.Drawing.Imaging.ImageFormat.Png);
    }
}

