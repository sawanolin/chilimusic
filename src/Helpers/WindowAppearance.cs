using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ChiliMusic;

internal static class WindowAppearance
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    public static void Attach(Window window)
    {
        if (window is DialogShell or SettingsWindow or InfoWindow)
            window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && System.Windows.Input.Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true }) { window.Close(); e.Handled = true; } };
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) => Apply(window);
        window.Loaded += (_, _) => Apply(window);
        window.SizeChanged += (_, _) => Apply(window);
        window.StateChanged += (_, _) => { Apply(window); window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => FitWorkArea(window)); };
        if (window.Content is Border surface)
        {
            surface.SizeChanged += (_, _) => Apply(window);
            if (surface.Child is FrameworkElement content) content.SizeChanged += (_, _) => Apply(window);
        }
    }
    private static void FitWorkArea(Window window)
    {
        if (window.WindowState != WindowState.Maximized) return;
        var hwnd = new WindowInteropHelper(window).Handle; var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var r = info.Work;
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0014);
    }
    public static void Apply(Window window)
    {
        if (window.Content is Border surface && surface.Child is FrameworkElement content)
        {
            double radius = window.WindowState == WindowState.Maximized ? 0 : 10;
            surface.CornerRadius = new CornerRadius(radius);
            double inner = Math.Max(0, radius - surface.BorderThickness.Left);
            if (content.ActualWidth > 0 && content.ActualHeight > 0)
                content.Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, content.ActualWidth, content.ActualHeight), inner, inner);
        }
        if (window.AllowsTransparency) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = Theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
    }
    public static void Capture(Window window, string path)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out var rect)) return;
        using var image = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, image.Size);
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
