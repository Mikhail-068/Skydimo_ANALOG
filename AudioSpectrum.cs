using System.Numerics;

namespace Kontur;

internal sealed record AudioBands(double Bass, double Mid, double Treble)
{
    public static readonly AudioBands Silence = new(0, 0, 0);

    public AudioBands Follow(AudioBands next, double attack, double release)
    {
        double Envelope(double value, double target) =>
            value + (target - value) * (target > value ? attack : release);
        return new(Envelope(Bass, next.Bass), Envelope(Mid, next.Mid), Envelope(Treble, next.Treble));
    }

    public AudioBands Accentuate(AudioBands recent)
    {
        static double Contrast(double signal, double baseline)
        {
            var level = 1.35 * Math.Pow(signal, 1.7);
            var accent = Math.Max(0, signal - baseline) * .85;
            return Math.Min(1, level + accent);
        }

        return new(Contrast(Bass, recent.Bass), Contrast(Mid, recent.Mid), Contrast(Treble, recent.Treble));
    }
}

internal sealed class AudioSpectrum
{
    internal const int WindowSize = 2048;
    private static readonly double[] Window = Enumerable.Range(0, WindowSize)
        .Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (WindowSize - 1))).ToArray();
    private static readonly Complex[] Roots = Enumerable.Range(0, WindowSize / 2)
        .Select(i => Complex.FromPolarCoordinates(1, -2 * Math.PI * i / WindowSize)).ToArray();

    private readonly float[] samples = new float[WindowSize];
    private readonly Complex[] bins = new Complex[WindowSize];
    private int cursor;
    private int sampleCount;

    public void Add(float sample)
    {
        samples[cursor] = sample;
        cursor = (cursor + 1) % WindowSize;
        sampleCount = Math.Min(sampleCount + 1, WindowSize);
    }

    public void Reset() => sampleCount = 0;

    public AudioBands Measure(int sampleRate)
    {
        if (sampleCount < WindowSize)
            return AudioBands.Silence;

        var reversed = 0;
        for (var i = 0; i < WindowSize; i++)
        {
            bins[reversed] = samples[(cursor + i) % WindowSize] * Window[i];
            var bit = WindowSize / 2;
            while ((reversed & bit) != 0)
            {
                reversed ^= bit;
                bit >>= 1;
            }
            reversed ^= bit;
        }

        for (var length = 2; length <= WindowSize; length *= 2)
        {
            var half = length / 2;
            for (var start = 0; start < WindowSize; start += length)
            {
                for (var i = 0; i < half; i++)
                {
                    var even = bins[start + i];
                    var odd = bins[start + i + half] * Roots[i * (WindowSize / length)];
                    bins[start + i] = even + odd;
                    bins[start + i + half] = even - odd;
                }
            }
        }

        double bass = 0, mid = 0, treble = 0;
        for (var i = 1; i < WindowSize / 2; i++)
        {
            var frequency = i * sampleRate / (double)WindowSize;
            if (frequency < 40)
                continue;
            if (frequency >= 10000)
                break;
            var power = bins[i].Real * bins[i].Real + bins[i].Imaginary * bins[i].Imaginary;
            if (frequency < 250)
                bass += power;
            else if (frequency < 2200)
                mid += power;
            else
                treble += power;
        }

        static double Level(double power, double gain)
        {
            var amplitude = Math.Max(0, Math.Sqrt(power) * 2 / WindowSize - .0008);
            return 1 - Math.Exp(-gain * Math.Pow(amplitude, .65));
        }
        return new(Level(bass, 3), Level(mid, 4), Level(treble, 6));
    }
}
