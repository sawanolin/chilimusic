using Microsoft.Win32;
using System.Windows.Media;
namespace ChiliMusic;
public static class Theme
{
    public static event Action? Updated;
    public static bool IsDark { get; private set; }
    private static Color? _coverAccent;
    public static ColorTheme Current { get; private set; } = ThemeCatalog.All[0];
    public static void Apply(string mode)
    {
        mode = ThemeCatalog.Normalize(mode); bool dark = false;
        if (mode == "System") try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = (int?)key?.GetValue("AppsUseLightTheme") == 0; } catch { }
        Current = ThemeCatalog.Get(mode == "System" ? dark ? "ink_blue" : "moon_white" : mode); IsDark = Current.Dark;
        var colors = new[] { Current.Background, Current.Surface, Current.Text, Current.Muted, Current.Border, Current.Selected, Current.Accent };
        string[] extraKeys = ["HeaderBrush", "SidebarBrush", "QueueBrush", "SelectedBrush", "QualityBrush"];
        string[] extraColors = [Current.Surface, Current.Sidebar, IsDark ? Current.Input : Current.Surface, Current.Selected, IsDark ? "#86EFAC" : "#166534"];
        for (int i = 0; i < extraKeys.Length; i++) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extraColors[i])); b.Freeze(); Application.Current.Resources[extraKeys[i]] = b; }
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "HoverBrush", "AccentBrush"];
        for (int i = 0; i < keys.Length; i++) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze(); Application.Current.Resources[keys[i]] = brush; }
        Application.Current.Resources["InputBrush"] = new SolidColorBrush(Parse(Current.Input));
        Application.Current.Resources["MutedBrush"] = new SolidColorBrush(Readable(Parse(Current.Muted), Parse(Current.Background), Parse(Current.Surface), Parse(Current.Selected)));
        ApplyAccent();
        foreach (var window in Application.Current.Windows.Cast<System.Windows.Window>()) WindowAppearance.Apply(window);
        Updated?.Invoke();
    }
    public static void SetCoverAccent(System.Windows.Media.Imaging.BitmapSource? cover, bool enabled)
    {
        _coverAccent = null;
        if (enabled && cover != null)
        {
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(cover, PixelFormats.Bgra32, null, 0); int stride = converted.PixelWidth * 4; var bytes = new byte[stride * converted.PixelHeight]; converted.CopyPixels(bytes, stride, 0);
            long r = 0, g = 0, b = 0, weight = 0;
            for (int i = 0; i < bytes.Length; i += Math.Max(4, (bytes.Length / 1600 / 4) * 4)) { int red = bytes[i + 2], green = bytes[i + 1], blue = bytes[i]; int saturation = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)); if (bytes[i + 3] < 128 || saturation < 35) continue; r += red * saturation; g += green * saturation; b += blue * saturation; weight += saturation; }
            if (weight > 0) { double rr = r / (double)weight, gg = g / (double)weight, bb = b / (double)weight; double luminance = .2126 * rr + .7152 * gg + .0722 * bb; double scale = IsDark ? Math.Max(1, 150 / Math.Max(1, luminance)) : Math.Min(1, 105 / Math.Max(1, luminance)); _coverAccent = Color.FromRgb((byte)Math.Clamp(rr * scale, 0, 255), (byte)Math.Clamp(gg * scale, 0, 255), (byte)Math.Clamp(bb * scale, 0, 255)); }
        }
        ApplyAccent();
    }
    private static void ApplyAccent()
    {
        Color fill;
        if (_coverAccent is not Color c)
        {
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(Readable(Parse(Current.Accent), Parse(Current.Background), Parse(Current.Surface), Parse(Current.Selected)));
            Application.Current.Resources["SelectedBrush"] = new SolidColorBrush(Parse(Current.Selected));
            fill = Parse(Current.Accent);
        }
        else
        {
            var selectedColor = Blend(Parse(Current.Background), c, IsDark ? .23 : .10);
            var brush = new SolidColorBrush(Readable(c, Parse(Current.Background), Parse(Current.Surface), selectedColor)); brush.Freeze(); Application.Current.Resources["AccentBrush"] = brush;
            var selected = new SolidColorBrush(selectedColor); selected.Freeze(); Application.Current.Resources["SelectedBrush"] = selected;
            fill = c;
        }
        // Keep text readable on the filled buttons for both themes and cover accents.
        var darkText = Color.FromRgb(14, 23, 40);
        bool white = Contrast(fill, Colors.White) >= Contrast(fill, darkText);
        if (!white && Contrast(fill, darkText) < 4.5) darkText = Colors.Black;
        Color Mix(Color color, double amount) => Color.FromRgb((byte)(color.R * (1 - amount) + (white ? 0 : 255) * amount), (byte)(color.G * (1 - amount) + (white ? 0 : 255) * amount), (byte)(color.B * (1 - amount) + (white ? 0 : 255) * amount));
        Set("AccentFillBrush", fill); Set("AccentForegroundBrush", white ? Colors.White : darkText); Set("AccentHoverBrush", Mix(fill, .10)); Set("AccentPressedBrush", Mix(fill, .18));
        static void Set(string key, Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); Application.Current.Resources[key] = brush; }
    }
    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R * (1 - t) + b.R * t), (byte)(a.G * (1 - t) + b.G * t), (byte)(a.B * (1 - t) + b.B * t));
    private static Color Readable(Color color, params Color[] backgrounds)
    {
        Color target = IsDark ? Colors.White : Colors.Black;
        for (int i = 0; i < 30 && backgrounds.Any(bg => Contrast(color, bg) < 4.5); i++) color = Blend(color, target, .06);
        return color;
    }
    public static Dictionary<string, string> WebColors() => new Dictionary<string, string> { ["bg"] = "BackgroundBrush", ["surface"] = "SurfaceBrush", ["input"] = "InputBrush", ["text"] = "TextBrush", ["muted"] = "MutedBrush", ["border"] = "BorderBrush", ["accent"] = "AccentFillBrush", ["accentText"] = "AccentBrush", ["accentFg"] = "AccentForegroundBrush", ["selected"] = "SelectedBrush" }.ToDictionary(p => p.Key, p => ((SolidColorBrush)Application.Current.Resources[p.Value]).Color.ToString().Replace("#FF", "#"));
    internal static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color) { static double Linear(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B); }
        double a = Luminance(first), b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
