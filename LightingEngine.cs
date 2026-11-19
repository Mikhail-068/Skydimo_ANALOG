using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kontur;

internal sealed record LightingInput(LightSettings Settings, Rectangle Display, bool IsOn, string? Port);
internal sealed record LinkStatus(bool Connected, string Message);

internal sealed class LightingEngine : IDisposable
{
    private static readonly Rgb[] Dark = new Rgb[Sk0127.Count];
    private static readonly byte[] OffFrame = Sk0127.Encode(Dark);
    private readonly LocalFiles files;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task captureTask;
    private readonly Task outputTask;
    private LightingInput input;
    private LinkStatus linkStatus = new(false, "Предпросмотр · лента не подключена");
    private string? captureError;
    private Rgb[]? screenPixels;
    private Rgb[] preview = Dark;

    public LightingEngine(LightingInput initialInput, LocalFiles files)
    {
        input = initialInput;
        this.files = files;
        captureTask = Task.Run(() => Capture(shutdown.Token));
        outputTask = Task.Run(() => Output(shutdown.Token));
    }

    public LinkStatus Status => Volatile.Read(ref linkStatus);
    public string? CaptureError => Volatile.Read(ref captureError);
    public Rgb[] Preview => Volatile.Read(ref preview);
    public void Update(LightingInput value) => Volatile.Write(ref input, value);

    private async Task Capture(CancellationToken stop)
    {
        using var capture = new DesktopCapture();
        Rgb[]? past = null;
        Rectangle pastDisplay = default;
        PointF[]? pastPath = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var current = Volatile.Read(ref input);
                if (current.Settings.Mode != LightMode.Screen || !current.IsOn)
                {
                    past = null;
                    Volatile.Write(ref screenPixels, null);
                    Volatile.Write(ref captureError, null);
                    continue;
                }

                try
                {
                    if (current.Display.IsEmpty)
                        throw new InvalidOperationException("Выбери подключённый монитор.");
                    var path = current.Settings.StripPath;
                    var fresh = capture.Read(current.Display, path);
                    if (past is not null && current.Display == pastDisplay && ReferenceEquals(path, pastPath))
                    {
                        for (var i = 0; i < fresh.Length; i++)
                            fresh[i] = Smooth(past[i], fresh[i]);
                    }
                    past = fresh;
                    pastDisplay = current.Display;
                    pastPath = path;
                    Volatile.Write(ref screenPixels, fresh);
                    Volatile.Write(ref captureError, null);
                }
                catch (Exception ex) when (ex is Win32Exception or ExternalException or InvalidOperationException or ArgumentException)
                {
                    var message = "Экран недоступен. Проверь выбор монитора.";
                    if (CaptureError != message)
                        files.Log($"Захват экрана: {ex.Message}");
                    Volatile.Write(ref captureError, message);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async Task Output(CancellationToken stop)
    {
        SerialLink? link = null;
        string? activePort = null;
        var retryAt = 0L;
        var lastWrite = 0L;
        var lastDelayLog = -5000L;
        var clock = Stopwatch.StartNew();
        var previewFade = new PowerFade();
        var stripFade = new PowerFade();
        var colors = Dark;
        var reverse = false;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var current = Volatile.Read(ref input);
                var now = clock.ElapsedMilliseconds;
                if (current.IsOn)
                {
                    colors = Colors(current, Volatile.Read(ref screenPixels));
                    reverse = current.Settings.StripPath is null && current.Settings.Reverse && current.Settings.Mode == LightMode.Screen;
                }
                Volatile.Write(ref preview, Scale(colors, previewFade.Sample(current.IsOn, now)));

                // Keep the existing connection open while the last frame fades to black.
                var port = !current.IsOn && link is not null && stripFade.Sample(false, now) > 0
                    ? activePort : current.Port;
                if (!string.Equals(activePort, port, StringComparison.OrdinalIgnoreCase))
                {
                    CloseLink(link);
                    link = null;
                    activePort = port;
                    stripFade = new PowerFade();
                    retryAt = 0;
                    lastWrite = 0;
                }
                if (activePort is null)
                {
                    SetStatus(false, "Предпросмотр · лента не подключена");
                    continue;
                }

                try
                {
                    if (link is null)
                    {
                        if (clock.ElapsedMilliseconds < retryAt)
                            continue;
                        if (retryAt == 0)
                            SetStatus(false, $"Подключение к {activePort}…", log: false);
                        var candidates = SerialLink.ControllerPorts();
                        if (candidates.Length != 1)
                            throw new IOException(candidates.Length == 0 ? "Контроллер SK0127 не найден. Проверь USB." : "Обнаружено несколько контроллеров CH340.");
                        var detectedPort = candidates[0];
                        link = SerialLink.Connect(detectedPort, stop);
                        files.Log($"Контроллер найден на {detectedPort}");
                        var latest = Volatile.Read(ref input);
                        if (!string.Equals(latest.Port, activePort, StringComparison.OrdinalIgnoreCase))
                        {
                            link.Dispose();
                            link = null;
                            continue;
                        }
                        current = latest;
                        colors = Colors(current, Volatile.Read(ref screenPixels));
                        reverse = current.Settings.StripPath is null && current.Settings.Reverse && current.Settings.Mode == LightMode.Screen;
                        stripFade = new PowerFade();
                        retryAt = 0;
                    }
                    var level = stripFade.Sample(current.IsOn, clock.ElapsedMilliseconds);
                    link.Write(Sk0127.Encode(Scale(colors, level), reverse));
                    now = clock.ElapsedMilliseconds;
                    var gap = now - lastWrite;
                    if (lastWrite > 0 && gap >= 150 && now - lastDelayLog >= 5000)
                    {
                        files.Log($"Пауза между отправленными кадрами: {gap} мс ({activePort}).");
                        lastDelayLog = now;
                    }
                    lastWrite = now;
                    SetStatus(true, $"SK0127 подключён · {activePort}");
                }
                catch (Exception ex) when (ex is IOException or Win32Exception)
                {
                    link?.Dispose();
                    link = null;
                    lastWrite = 0;
                    retryAt = clock.ElapsedMilliseconds + 2500;
                    SetStatus(false, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            CloseLink(link);
        }
    }

    private void CloseLink(SerialLink? link)
    {
        if (link is null)
            return;
        using (link)
        {
            try { link.Write(OffFrame); }
            catch (Exception ex) when (ex is IOException or Win32Exception)
            {
                files.Log("При отключении не удалось погасить ленту. " + ex.Message);
            }
        }
    }

    private void SetStatus(bool connected, string message, bool log = true)
    {
        if (Status.Message == message)
            return;
        Volatile.Write(ref linkStatus, new(connected, message));
        if (log)
            files.Log(message);
    }

    private static Rgb[] Colors(LightingInput current, Rgb[]? screen)
    {
        var settings = current.Settings;
        if (settings.Mode == LightMode.Screen && screen is not null)
            return screen.Select(pixel => Sk0127.Correct(pixel, settings.Brightness)).ToArray();
        var solid = Sk0127.Correct(Rgb.FromColor(Color.FromArgb(settings.ColorArgb)), settings.Brightness);
        return Enumerable.Repeat(solid, Sk0127.Count).ToArray();
    }

    private static Rgb[] Scale(Rgb[] colors, double level)
    {
        if (level <= 0)
            return Dark;
        if (level >= 1)
            return colors;

        // Match the gamma correction used for the brightness slider.
        var multiplier = Math.Pow(level, 2.2);
        byte Channel(byte value) => (byte)Math.Round(value * multiplier);
        return colors.Select(color => new Rgb(Channel(color.Red), Channel(color.Green), Channel(color.Blue))).ToArray();
    }

    private sealed class PowerFade
    {
        private bool on;
        private double startLevel;
        private long startedAt;

        public double Sample(bool enabled, long now)
        {
            var duration = on ? 650 : 800;
            var progress = Math.Clamp((now - startedAt) / (double)duration, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);
            var level = startLevel + ((on ? 1 : 0) - startLevel) * eased;
            if (enabled != on)
            {
                startLevel = level;
                startedAt = now;
                on = enabled;
            }
            return level;
        }
    }

    private static Rgb Smooth(Rgb old, Rgb now)
    {
        static byte Mix(byte a, byte b) => (byte)Math.Round(a * 0.63 + b * 0.37);
        return new(Mix(old.Red, now.Red), Mix(old.Green, now.Green), Mix(old.Blue, now.Blue));
    }

    public void Dispose()
    {
        shutdown.Cancel();
        var work = Task.WhenAll(captureTask, outputTask);
        if (!work.Wait(TimeSpan.FromSeconds(1)))
            _ = work.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
        else
            shutdown.Dispose();
    }
}
