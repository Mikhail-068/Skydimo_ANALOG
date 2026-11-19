using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Kontur;

internal sealed class DesktopCapture : IDisposable
{
    private const int CellSize = 5;
    private const int Width = Sk0127.Columns * CellSize;
    private const int Height = Sk0127.Rows * CellSize;
    private const uint SourceCopy = 0x00CC0020;
    private readonly Bitmap bitmap = new(Width, Height, PixelFormat.Format32bppRgb);
    private readonly byte[] buffer = new byte[Width * Height * 4];

    public Rgb[] Read(Rectangle display, PointF[]? stripPath = null)
    {
        Stretch(display);
        var bits = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(bits.Scan0 + y * bits.Stride, buffer, y * Width * 4, Width * 4);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        if (stripPath is { Length: Sk0127.Count })
            return stripPath.Select(point => Average(
                Math.Clamp((int)Math.Round((1 - point.X) * (Sk0127.Columns - 1)), 0, Sk0127.Columns - 1),
                Math.Clamp((int)Math.Round(point.Y * (Sk0127.Rows - 1)), 0, Sk0127.Rows - 1))).ToArray();

        return Sk0127.Cells.Select(point => Average(point.X, point.Y)).ToArray();
    }

    private void Stretch(Rectangle display)
    {
        var desktop = GetDC(IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            using var target = Graphics.FromImage(bitmap);
            var surface = target.GetHdc();
            try
            {
                if (SetStretchBltMode(surface, 4) == 0 || // HALFTONE
                    !SetBrushOrgEx(surface, 0, 0, IntPtr.Zero) ||
                    !StretchBlt(surface, 0, 0, Width, Height, desktop,
                        display.X, display.Y, display.Width, display.Height, SourceCopy))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                target.ReleaseHdc(surface);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, desktop);
        }
    }

    private Rgb Average(int cellX, int cellY)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        for (var y = cellY * CellSize; y < (cellY + 1) * CellSize; y++)
        {
            for (var x = cellX * CellSize; x < (cellX + 1) * CellSize; x++)
            {
                var index = (y * Width + x) * 4;
                blue += buffer[index];
                green += buffer[index + 1];
                red += buffer[index + 2];
            }
        }

        var count = CellSize * CellSize;
        return new Rgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    public void Dispose() => bitmap.Dispose();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetStretchBltMode(IntPtr device, int mode);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetBrushOrgEx(IntPtr device, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);
}
