using System.Runtime.InteropServices;

namespace CrosshairOverlay.Interop;

internal static partial class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern uint GetPixel(nint hdc, int x, int y);
}
