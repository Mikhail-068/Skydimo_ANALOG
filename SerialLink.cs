using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Kontur;

internal sealed class SerialLink : IDisposable
{
    internal const string Automatic = "auto";
    private readonly SafeFileHandle handle;

    private SerialLink(SafeFileHandle handle) => this.handle = handle;

    public static bool IsPort(string? name) =>
        name is not null && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        name.Length is >= 4 and <= 8 && name.AsSpan(3).IndexOfAnyExceptInRange('0', '9') == -1 &&
        int.TryParse(name.AsSpan(3), out var number) && number > 0;

    public static string[] Ports()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
            return [];
        return key.GetValueNames().Select(name => key.GetValue(name) as string)
            .OfType<string>().Where(IsPort).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => int.Parse(name.AsSpan(3))).ToArray();
    }

    public static string[] ControllerPorts()
    {
        using var devices = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_7523");
        if (devices is null) return [];
        var available = Ports();
        var controllerPorts = devices.GetSubKeyNames().Select(id =>
        {
            using var parameters = devices.OpenSubKey(id + @"\Device Parameters");
            return parameters?.GetValue("PortName") as string;
        }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return available.Where(controllerPorts.Contains).ToArray();
    }

    internal static string SelectControllerPort(string selection, string[] candidates)
    {
        if (!string.Equals(selection, Automatic, StringComparison.OrdinalIgnoreCase))
        {
            return candidates.FirstOrDefault(port => string.Equals(port, selection, StringComparison.OrdinalIgnoreCase))
                ?? throw new IOException($"{selection} недоступен. Проверь USB или выбери другой порт.");
        }

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new IOException("Контроллер CH340 не найден. Проверь USB."),
            _ => throw new IOException("Найдено несколько CH340. Выбери COM-порт контроллера.")
        };
    }

    public static SerialLink Connect(string port, CancellationToken stop)
    {
        if (!IsPort(port))
            throw new IOException("Выбери COM-порт контроллера.");

        var handle = CreateFile(@"\\.\" + port, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error is 5 or 32)
                throw new IOException($"{port} занят. Закрой другую программу подсветки.");
            if (error is 2 or 3 or 433 or 1167)
                throw new IOException($"{port} недоступен. Проверь USB и номер порта.");
            throw new Win32Exception(error);
        }

        var link = new SerialLink(handle);
        try
        {
            var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
            Check(GetCommState(handle, ref dcb));
            dcb.Baud = 115200;
            dcb.Flags = 1 | (1u << 4) | (1u << 12); // Binary, DTR enabled, RTS enabled, no flow control.
            dcb.Bits = 8;
            dcb.Parity = 0;
            dcb.Stop = 0;
            Check(SetCommState(handle, ref dcb));
            var timeouts = new Timeouts { ReadInterval = uint.MaxValue, WriteConstant = 120 };
            Check(SetCommTimeouts(handle, ref timeouts));

            var packet = new byte[64];
            var reply = new byte[128];
            for (var attempt = 0; attempt < 2; attempt++)
            {
                stop.ThrowIfCancellationRequested();
                link.Write("Moni-A"u8.ToArray());
                var elapsed = Stopwatch.StartNew();
                var received = 0;
                while (elapsed.ElapsedMilliseconds < 420)
                {
                    var size = Math.Min(packet.Length, reply.Length - received);
                    Check(ReadFile(handle, packet, (uint)size, out var count, IntPtr.Zero));
                    packet.AsSpan(0, (int)count).CopyTo(reply.AsSpan(received));
                    received += (int)count;
                    if (Sk0127.Identified(reply.AsSpan(0, received)))
                        return link;
                    if (received == reply.Length)
                        break;
                    if (stop.WaitHandle.WaitOne(15))
                        stop.ThrowIfCancellationRequested();
                }
            }

            throw new IOException($"{port} не ответил как SK0127. Проверь номер порта.");
        }
        catch
        {
            link.Dispose();
            throw;
        }
    }

    public void Write(byte[] packet)
    {
        Check(WriteFile(handle, packet, (uint)packet.Length, out var count, IntPtr.Zero));
        if (count != packet.Length)
            throw new IOException($"Контроллер принял {count} из {packet.Length} байт.");
    }

    public void Dispose() => handle.Dispose();

    private static void Check(bool success)
    {
        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length, Baud, Flags;
        public ushort Reserved, XonLimit, XoffLimit;
        public byte Bits, Parity, Stop, Xon, Xoff, Error, Eof, Evt;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timeouts
    {
        public uint ReadInterval, ReadMultiplier, ReadConstant, WriteMultiplier, WriteConstant;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb state);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb state);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref Timeouts timeouts);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, uint size, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint size, out uint written, IntPtr overlapped);
}
