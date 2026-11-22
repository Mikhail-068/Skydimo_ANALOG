using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Kontur;

internal sealed class AudioLoopback : IDisposable
{
    private readonly AudioSpectrum spectrum = new();
    private readonly IDeviceEnumerator enumerator;
    private IAudioClient? client;
    private ICaptureClient? capture;
    private string? deviceId;
    private byte[] buffer = [];
    private int sampleRate;
    private int channels;
    private int blockSize;
    private int sampleSize;
    private bool floatingPoint;
    private bool started;
    private long nextDeviceCheck;
    private long lastPacket;
    private AudioBands bands = AudioBands.Silence;

    public AudioLoopback()
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
        enumerator = (IDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    public AudioBands Read(long now)
    {
        if (capture is null || now >= nextDeviceCheck)
            CheckDevice(now);

        var received = false;
        while (capture!.GetNextPacketSize() > 0)
        {
            capture.GetBuffer(out var source, out var frames, out var flags, IntPtr.Zero, IntPtr.Zero);
            try
            {
                if ((flags & 1) != 0)
                    spectrum.Reset();
                var size = checked((int)frames * blockSize);
                if ((flags & 2) != 0)
                {
                    for (var i = 0u; i < frames; i++)
                        spectrum.Add(0);
                }
                else
                {
                    if (buffer.Length < size)
                        buffer = new byte[size];
                    Marshal.Copy(source, buffer, 0, size);
                    for (var offset = 0; offset < size; offset += blockSize)
                    {
                        var sample = 0f;
                        for (var channel = 0; channel < channels; channel++)
                            sample += Decode(buffer.AsSpan(offset + channel * sampleSize, sampleSize));
                        spectrum.Add(sample / channels);
                    }
                }
            }
            finally { capture.ReleaseBuffer(frames); }
            received = true;
        }

        if (received)
        {
            lastPacket = now;
            bands = spectrum.Measure(sampleRate);
        }
        else if (now - lastPacket > 120)
        {
            spectrum.Reset();
            bands = AudioBands.Silence;
        }
        return bands;
    }

    private float Decode(ReadOnlySpan<byte> sample)
    {
        if (floatingPoint)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(sample);
            return float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
        }

        return sampleSize switch
        {
            1 => (sample[0] - 128) / 128f,
            2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
            3 => (sample[0] | sample[1] << 8 | (sbyte)sample[2] << 16) / 8388608f,
            _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f
        };
    }

    private void CheckDevice(long now)
    {
        nextDeviceCheck = now + 1000;
        var device = enumerator.GetDefaultAudioEndpoint(0, 1);
        try
        {
            var id = device.GetId();
            if (id == deviceId && capture is not null)
                return;

            CloseStream();
            spectrum.Reset();
            bands = AudioBands.Silence;
            lastPacket = now;

            var iid = typeof(IAudioClient).GUID;
            client = (IAudioClient)device.Activate(ref iid, 23, IntPtr.Zero);
            var format = client.GetMixFormat();
            try
            {
                ReadFormat(format);
                const int loopback = 0x00020000;
                client.Initialize(0, loopback, 1_000_000, 0, format, IntPtr.Zero);
            }
            finally { Marshal.FreeCoTaskMem(format); }

            iid = typeof(ICaptureClient).GUID;
            capture = (ICaptureClient)client.GetService(ref iid);
            client.Start();
            started = true;
            deviceId = id;
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    private void ReadFormat(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format);
        channels = (ushort)Marshal.ReadInt16(format, 2);
        sampleRate = Marshal.ReadInt32(format, 4);
        blockSize = (ushort)Marshal.ReadInt16(format, 12);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        sampleSize = bits / 8;
        if (tag == 0xfffe && (ushort)Marshal.ReadInt16(format, 16) >= 22)
        {
            var subFormat = Marshal.PtrToStructure<Guid>(IntPtr.Add(format, 24));
            if (subFormat == new Guid("00000001-0000-0010-8000-00AA00389B71"))
                tag = 1;
            else if (subFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"))
                tag = 3;
        }

        floatingPoint = tag == 3;
        var supported = floatingPoint ? bits == 32 : tag == 1 && bits is 8 or 16 or 24 or 32;
        if (!supported || channels == 0 || sampleRate <= 0 || blockSize < channels * sampleSize)
            throw new NotSupportedException($"Аудиоформат Windows: {tag}, {channels} кан., {sampleRate} Гц, {bits} бит.");
    }

    private void CloseStream()
    {
        if (started)
        {
            try { client!.Stop(); }
            catch (COMException) { } // The endpoint can already be disconnected.
            started = false;
        }
        if (capture is not null)
        {
            Marshal.ReleaseComObject(capture);
            capture = null;
        }
        if (client is not null)
        {
            Marshal.ReleaseComObject(client);
            client = null;
        }
        deviceId = null;
    }

    public void Dispose()
    {
        CloseStream();
        Marshal.ReleaseComObject(enumerator);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints();
        IDevice GetDefaultAudioEndpoint(int flow, int role);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object Activate(ref Guid iid, int context, IntPtr activationParameters);
        void OpenPropertyStore();
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        void Initialize(int shareMode, int flags, long duration, long periodicity, IntPtr format, IntPtr session);
        void GetBufferSize();
        void GetStreamLatency();
        void GetCurrentPadding();
        void IsFormatSupported();
        IntPtr GetMixFormat();
        void GetDevicePeriod();
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetService(ref Guid iid);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, IntPtr devicePosition, IntPtr timestamp);
        void ReleaseBuffer(uint frames);
        uint GetNextPacketSize();
    }
}
