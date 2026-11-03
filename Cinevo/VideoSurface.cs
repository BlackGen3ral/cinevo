using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace Cinevo;
public sealed class VideoSurface : HwndHost
{
    public IntPtr VideoHandle { get; private set; }
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        VideoHandle = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (VideoHandle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        return new HandleRef(this, VideoHandle);
    }
    protected override void DestroyWindowCore(HandleRef hwnd) { DestroyWindow(hwnd.Handle); VideoHandle = IntPtr.Zero; }
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
}
