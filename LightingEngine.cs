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
    private readonly Task audioTask;
    private LightingInput input;
    private LinkStatus linkStatus = new(false, "Предпросмотр · лента не подключена");
    private string? captureError;
    private string? audioError;
    private AudioBands music = AudioBands.Silence;
    private ScreenFrame? screenFrame;

    private sealed record ScreenFrame(Rectangle Display, PointF[]? Path, Rgb[] Pixels, long CapturedAt)
    {
        public bool Matches(LightingInput input) => Display == input.Display && ReferenceEquals(Path, input.Settings.StripPath);
    }
    private Rgb[] preview = Dark;

    public LightingEngine(LightingInput initialInput, LocalFiles files, Func<Rectangle, PointF[]?, Rgb[]>? readScreen = null)
    {
        input = initialInput;
        this.files = files;
        captureTask = Task.Run(() => Capture(readScreen, shutdown.Token));
        outputTask = Task.Run(() => Output(shutdown.Token));
        // WASAPI services must be released on the same thread that requested them.
        audioTask = Task.Factory.StartNew(() => CaptureAudio(shutdown.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public LinkStatus Status => Volatile.Read(ref linkStatus);
    public string? CaptureError => Volatile.Read(ref captureError);
    public string? AudioError => Volatile.Read(ref audioError);
    public Rgb[] Preview => Volatile.Read(ref preview);
    public void Update(LightingInput value) => Volatile.Write(ref input, value);

    private async Task Capture(Func<Rectangle, PointF[]?, Rgb[]>? readScreen, CancellationToken stop)
    {
        using var capture = readScreen is null ? new DesktopCapture() : null;
        readScreen ??= capture!.Read;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var current = Volatile.Read(ref input);
                if (current.Settings.Mode != LightMode.Screen || !current.IsOn)
                {
                    Volatile.Write(ref screenFrame, null);
                    Volatile.Write(ref captureError, null);
                    continue;
                }

                try
                {
                    if (current.Display.IsEmpty)
                        throw new InvalidOperationException("Выбери подключённый монитор.");
                    var path = current.Settings.StripPath;
                    var fresh = readScreen(current.Display, path);
                    var past = Volatile.Read(ref screenFrame);
                    if (past?.Matches(current) == true)
                    {
                        for (var i = 0; i < fresh.Length; i++)
                            fresh[i] = Smooth(past.Pixels[i], fresh[i]);
                    }
                    Volatile.Write(ref screenFrame, new(current.Display, path, fresh, Environment.TickCount64));
                    Volatile.Write(ref captureError, null);
                }
                catch (Exception ex) when (ex is Win32Exception or ExternalException or InvalidOperationException or ArgumentException)
                {
                    Volatile.Write(ref screenFrame, null);
                    var message = "Экран недоступен. Проверь выбор монитора.";
                    if (CaptureError != message)
                        files.Log($"Захват экрана: {ex.Message}");
                    Volatile.Write(ref captureError, message);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private void CaptureAudio(CancellationToken stop)
    {
        AudioLoopback? audio = null;
        var bands = AudioBands.Silence;
        var recent = AudioBands.Silence;
        var retryAt = 0L;
        try
        {
            while (!stop.WaitHandle.WaitOne(25))
            {
                var current = Volatile.Read(ref input);
                if (!current.IsOn || current.Settings.Mode != LightMode.Music)
                {
                    audio?.Dispose();
                    audio = null;
                    bands = recent = AudioBands.Silence;
                    retryAt = 0;
                    Volatile.Write(ref music, bands);
                    Volatile.Write(ref audioError, null);
                    continue;
                }

                var now = Environment.TickCount64;
                if (now < retryAt)
                    continue;

                try
                {
                    audio ??= new AudioLoopback();
                    bands = bands.Follow(audio.Read(now), attack: .86, release: .48);
                    Volatile.Write(ref music, bands.Accentuate(recent));
                    recent = recent.Follow(bands, attack: .055, release: .026);
                    Volatile.Write(ref audioError, null);
                }
                catch (Exception ex) when (ex is COMException or NotSupportedException)
                {
                    audio?.Dispose();
                    audio = null;
                    bands = recent = AudioBands.Silence;
                    retryAt = now + 2000;
                    if (AudioError is null)
                        files.Log($"Захват звука Windows: {ex.Message}");
                    Volatile.Write(ref music, bands);
                    Volatile.Write(ref audioError, "Не удалось получить звук с аудиовыхода Windows.");
                }
            }
        }
        finally { audio?.Dispose(); }
    }

    private async Task Output(CancellationToken stop)
    {
        SerialLink? link = null;
        string? activePort = null;
        string? connectedPort = null;
        var retryAt = 0L;
        var lastWrite = 0L;
        var lastDelayLog = -5000L;
        var clock = Stopwatch.StartNew();
        var previewFade = new PowerFade();
        var stripFade = new PowerFade();
        var colors = Dark;
        var reverse = false;
        long? hazardStart = null;

        bool UpdateFrame(LightingInput current)
        {
            if (!current.IsOn || current.Settings.Mode != LightMode.Hazard)
                hazardStart = null;
            if (!current.IsOn)
                return false;

            var now = clock.ElapsedMilliseconds;
            var next = current.Settings.Mode == LightMode.Hazard
                ? HazardFrame(current.Settings, (now - (hazardStart ??= now)) / 1000d)
                : Colors(current, Volatile.Read(ref screenFrame), clock.Elapsed.TotalSeconds, Volatile.Read(ref music));
            if (next is null)
                return false;

            colors = next;
            reverse = current.Settings.StripPath is null && current.Settings.Reverse && current.Settings.Mode != LightMode.Solid;
            return true;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var current = Volatile.Read(ref input);
                var now = clock.ElapsedMilliseconds;
                var light = UpdateFrame(current);
                Volatile.Write(ref preview, Scale(colors, previewFade.Sample(light, now)));

                // Keep the existing connection open while the last frame fades to black.
                var port = !current.IsOn && link is not null && stripFade.Sample(false, now) > 0
                    ? activePort : current.Port;
                if (!string.Equals(activePort, port, StringComparison.OrdinalIgnoreCase))
                {
                    CloseLink(link);
                    link = null;
                    activePort = port;
                    connectedPort = null;
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
                        var detectedPort = SerialLink.SelectControllerPort(activePort, SerialLink.ControllerPorts());
                        SetStatus(false, $"Подключение к {detectedPort}…", log: false);
                        link = SerialLink.Connect(detectedPort, stop);
                        connectedPort = detectedPort;
                        files.Log($"Контроллер найден на {detectedPort}");
                        var latest = Volatile.Read(ref input);
                        if (!string.Equals(latest.Port, activePort, StringComparison.OrdinalIgnoreCase))
                        {
                            link.Dispose();
                            link = null;
                            continue;
                        }
                        current = latest;
                        light = UpdateFrame(current);
                        stripFade = new PowerFade();
                        retryAt = 0;
                    }
                    var level = stripFade.Sample(light, clock.ElapsedMilliseconds);
                    link.Write(Sk0127.Encode(Scale(colors, level), reverse));
                    now = clock.ElapsedMilliseconds;
                    var gap = now - lastWrite;
                    if (lastWrite > 0 && gap >= 150 && now - lastDelayLog >= 5000)
                    {
                        files.Log($"Пауза между отправленными кадрами: {gap} мс ({connectedPort}).");
                        lastDelayLog = now;
                    }
                    lastWrite = now;
                    SetStatus(true, $"SK0127 подключён · {connectedPort}");
                }
                catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    link?.Dispose();
                    link = null;
                    connectedPort = null;
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

    private static Rgb[]? Colors(LightingInput current, ScreenFrame? screen, double seconds, AudioBands music)
    {
        var settings = current.Settings;
        if (settings.Mode == LightMode.Screen)
        {
            if (screen?.Matches(current) != true || Environment.TickCount64 - screen.CapturedAt > 500)
                return null;

            return screen.Pixels.Select(pixel => Sk0127.Correct(pixel, settings.Brightness)).ToArray();
        }
        var color = Rgb.FromColor(Color.FromArgb(settings.ColorArgb));
        if (settings.Mode is LightMode.Music or LightMode.Magic or LightMode.Chase)
        {
            var frame = new Rgb[Sk0127.Count];
            for (var i = 0; i < frame.Length; i++)
            {
                var level = settings.Brightness;
                var shade = color;
                switch (settings.Mode)
                {
                    case LightMode.Music:
                        frame[i] = MusicPixel(music, i / (double)(frame.Length - 1), level);
                        continue;
                    case LightMode.Magic:
                        shade = Spectrum(seconds * .16 + i / (double)frame.Length);
                        break;
                    case LightMode.Chase:
                        var distance = (seconds * 28 - i + frame.Length) % frame.Length;
                        level = (int)Math.Round(level * Math.Pow(Math.Max(0, 1 - distance / 13), 1.4));
                        break;
                }
                frame[i] = Sk0127.Correct(shade, level);
            }
            if (settings.StripPath is null && settings.Reverse)
                Array.Reverse(frame);
            return frame;
        }
        var solid = Sk0127.Correct(color, settings.Brightness);
        return Enumerable.Repeat(solid, Sk0127.Count).ToArray();
    }

    internal static Rgb[] HazardFrame(LightSettings settings, double seconds)
    {
        const double rise = .65;
        const double holdEnd = rise + .32;
        const double fadeEnd = holdEnd + 1.35;
        const double cycle = fadeEnd + .44;

        var phase = seconds % cycle;
        // Both sides share one clock. Keep sending black during the pause.
        if (phase <= 0 || phase >= fadeEnd)
            return Dark;

        var fading = phase > holdEnd;
        var progress = fading
            ? (fadeEnd - phase) / (fadeEnd - holdEnd)
            : Math.Min(1, phase / rise);
        var level = progress * progress * (3 - 2 * progress);
        var peak = Sk0127.Correct(new Rgb(255, 177, 52), settings.Brightness);
        // Preserve dim steps during the fade instead of compressing them with full gamma.
        var amber = ScaleRgb(peak, Math.Pow(level, fading ? 1.25 : 2.2));
        if (amber == default)
            return Dark;
        if (settings.StripPath is not { Length: Sk0127.Count } path)
            return Sk0127.SideFrame(amber, amber);

        var frame = new Rgb[Sk0127.Count];
        var aspect = (Sk0127.Columns - 1d) / (Sk0127.Rows - 1);
        for (var i = 0; i < frame.Length; i++)
        {
            var (x, y) = (path[i].X, path[i].Y);
            var horizontalDistance = Math.Min(x, 1 - x) * aspect;
            if (horizontalDistance < Math.Min(y, 1 - y))
                frame[i] = amber;
        }
        return frame;
    }

    internal static Rgb MusicPixel(AudioBands bands, double position, int brightness)
    {
        double Weight(double center) => 1 - .82 * Math.Abs(position - center);
        var red = bands.Bass * Weight(0);
        var green = bands.Mid * Weight(.5);
        var blue = bands.Treble * Weight(1);
        var peak = Math.Max(red, Math.Max(green, blue));
        if (peak <= 0)
            return default;

        var limit = Sk0127.Correct(new Rgb(255, 255, 255), brightness).Red;
        // Remove the shared gray component but keep brightness tied to the strongest band.
        // The lower bound on the color range keeps near-equal bands from flickering between hues.
        var range = Math.Max(peak - Math.Min(red, Math.Min(green, blue)), peak * .045);
        byte Channel(double value)
        {
            var chroma = Math.Max(0, 1 - (peak - value) / range);
            return (byte)Math.Round(limit * peak * Math.Pow(chroma, 1.3));
        }
        return new(Channel(red), Channel(green), Channel(blue));
    }

    private static Rgb Spectrum(double phase)
    {
        var hue = phase % 1 * 6;
        byte Channel(int offset)
        {
            var k = (hue + offset) % 6;
            return (byte)Math.Round(255 * (1 - Math.Clamp(Math.Min(k, 4 - k), 0, 1)));
        }
        return new(Channel(5), Channel(3), Channel(1));
    }

    private static Rgb[] Scale(Rgb[] colors, double level)
    {
        if (level <= 0)
            return Dark;
        if (level >= 1)
            return colors;

        // Match the gamma correction used for the brightness slider.
        var multiplier = Math.Pow(level, 2.2);
        return colors.Select(color => ScaleRgb(color, multiplier)).ToArray();
    }

    private static Rgb ScaleRgb(Rgb color, double multiplier)
    {
        byte Channel(byte value) => (byte)Math.Round(value * multiplier);
        return new Rgb(Channel(color.Red), Channel(color.Green), Channel(color.Blue));
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
        var work = Task.WhenAll(captureTask, outputTask, audioTask);
        if (!work.Wait(TimeSpan.FromSeconds(1)))
            _ = work.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
        else
            shutdown.Dispose();
    }
}
