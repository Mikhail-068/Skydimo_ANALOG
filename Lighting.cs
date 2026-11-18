using System.Diagnostics;
using System.Text.Json;

namespace Kontur;

internal enum LightMode { Solid, Screen }

internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    public static Rgb FromColor(Color color) => new(color.R, color.G, color.B);
    public Color PreviewColor => Color.FromArgb(Preview(Red), Preview(Green), Preview(Blue));

    private static int Preview(byte channel) => (int)Math.Round(255 * Math.Pow(channel / 255d, 1 / 2.2));
}

internal static class Sk0127
{
    public const int Count = 65;
    public const int Columns = 31;
    public const int Rows = 17;
    public static readonly Point[] Cells = Enumerable.Range(0, Rows).Reverse().Select(y => new Point(0, y))
        .Concat(Enumerable.Range(0, Columns).Select(x => new Point(x, 0)))
        .Concat(Enumerable.Range(0, Rows).Select(y => new Point(Columns - 1, y))).ToArray();

    public static bool Identified(ReadOnlySpan<byte> reply)
    {
        var index = reply.IndexOf("SK0127,"u8);
        // Seven serial bytes follow the prefix. They can contain CR and LF.
        return index >= 0 && reply.Length >= index + 16 && reply.Slice(index + 14, 2).SequenceEqual("\r\n"u8);
    }

    public static int SourceIndex(int index, bool reverse) => reverse ? Count - 1 - index : index;

    public static byte[] Encode(Rgb[] pixels, bool reverse = false)
    {
        if (pixels.Length != Count)
            throw new ArgumentException("SK0127 ожидает 65 цветов.", nameof(pixels));

        var result = new byte[6 + Count * 3];
        "Ada"u8.CopyTo(result);
        result[5] = Count;
        for (var i = 0; i < Count; i++)
        {
            var pixel = pixels[SourceIndex(i, reverse)];
            var offset = 6 + 3 * i;
            result[offset] = pixel.Red;
            result[offset + 1] = pixel.Green;
            result[offset + 2] = pixel.Blue;
        }
        return result;
    }

    public static Rgb Correct(Rgb color, int brightness)
    {
        var multiplier = Math.Clamp(brightness, 0, 100) / 25500d;
        byte Scale(byte value) => (byte)(255 * Math.Pow(value * multiplier, 2.2));
        return new(Scale(color.Red), Scale(color.Green), Scale(color.Blue));
    }
}

internal sealed record LightSettings
{
    public string Port { get; init; } = "COM12";
    public string Monitor { get; init; } = "";
    public int Brightness { get; init; } = 44;
    public int ColorArgb { get; init; } = Color.FromArgb(154, 242, 120).ToArgb();
    public Dictionary<int, int>? PaletteOverrides { get; init; }
    public int? PaletteSlot { get; init; }
    public LightMode Mode { get; init; } = LightMode.Solid;
    public bool Reverse { get; init; }
}

internal sealed class LocalFiles
{
    private const int MaxLogBytes = 512_000;
    private readonly object logLock = new();
    private readonly string folder;
    public string LogFile => Path.Combine(folder, "kontur.log");
    private string SettingsFile => Path.Combine(folder, "settings.json");

    public LocalFiles(string folder) => this.folder = folder;

    public LightSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<LightSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log("Не удалось прочитать настройки. Загружены значения по умолчанию. " + ex.Message);
        }
        return new();
    }

    public void Save(LightSettings settings)
    {
        Directory.CreateDirectory(folder);
        var temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsFile, true);
    }

    public void Log(string message)
    {
        lock (logLock)
        {
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes)
                    File.Move(LogFile, LogFile + ".1", true);
                File.AppendAllText(LogFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}
