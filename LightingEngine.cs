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
                    var fresh = capture.Read(current.Display);
                    if (past is not null && current.Display == pastDisplay)
                    {
                        for (var i = 0; i < fresh.Length; i++)
                            fresh[i] = Smooth(past[i], fresh[i]);
                    }
                    past = fresh;
                    pastDisplay = current.Display;
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
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var current = Volatile.Read(ref input);
                var pixels = Colors(current, Volatile.Read(ref screenPixels));
                Volatile.Write(ref preview, pixels);

                if (!string.Equals(activePort, current.Port, StringComparison.OrdinalIgnoreCase))
                {
                    CloseLink(link);
                    link = null;
                    activePort = current.Port;
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
                        pixels = Colors(current, Volatile.Read(ref screenPixels));
                        retryAt = 0;
                    }
                    link.Write(Sk0127.Encode(pixels, current.Settings.Reverse && current.Settings.Mode == LightMode.Screen));
                    var now = clock.ElapsedMilliseconds;
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
        if (!current.IsOn)
            return Dark;
        var settings = current.Settings;
        if (settings.Mode == LightMode.Screen && screen is not null)
            return screen.Select(pixel => Sk0127.Correct(pixel, settings.Brightness)).ToArray();
        var solid = Sk0127.Correct(Rgb.FromColor(Color.FromArgb(settings.ColorArgb)), settings.Brightness);
        return Enumerable.Repeat(solid, Sk0127.Count).ToArray();
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
