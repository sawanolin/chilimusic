using Microsoft.Win32;
using System.Windows.Media;
namespace ChiliMusic;
public static class Theme
{
    public static bool IsDark { get; private set; }
    public static void Apply(string mode)
    {
        bool dark = mode == "Dark";
        if (mode == "System") try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = (int?)key?.GetValue("AppsUseLightTheme") == 0; } catch { }
        var colors = dark ? new[] { "#0E1728", "#18263C", "#F2F5FA", "#AEBBD0", "#2B415E", "#203B63", "#6F9FEF" } : new[] { "#EDF3F7", "#F9FBFC", "#263443", "#667085", "#D5E0E8", "#DDE9FB", "#3478F6" };
        IsDark = dark;
        string[] extraKeys = ["HeaderBrush", "SidebarBrush", "QueueBrush", "SelectedBrush", "QualityBrush"];
        string[] extraColors = dark ? ["#18263C", "#0E1728", "#101C2E", "#203B63", "#86EFAC"] : ["#F9FBFC", "#EDF3F7", "#F9FBFC", "#DDE9FB", "#166534"];
        for (int i = 0; i < extraKeys.Length; i++) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extraColors[i])); b.Freeze(); Application.Current.Resources[extraKeys[i]] = b; }
        foreach (var window in Application.Current.Windows.Cast<System.Windows.Window>()) WindowAppearance.Apply(window);
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "HoverBrush", "AccentBrush"];
        for (int i = 0; i < keys.Length; i++) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze(); Application.Current.Resources[keys[i]] = brush; }
    }
}
