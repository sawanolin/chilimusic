using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace ChiliMusic;
public static class UiVerification
{
    public static async Task RunLayoutAsync(App app, TaskbarService taskbar, string[] files)
    {
        var vm = app.Vm; app.ShowMain();
        foreach (var file in files.Where(File.Exists)) { await vm.OpenLocalFilesAsync([file]); await Task.Delay(1200); }
        vm.Player.Pause();
        var main = Application.Current.Windows.Cast<Window>().OfType<MainWindow>().First();
        var checks = new List<object>();
        foreach (var mode in new[] { "Light", "Dark" })
        {
            Theme.Apply(mode); main.Activate(); await Task.Delay(250);
            Capture(main, Path.Combine(Store.Root, $"main-{mode.ToLowerInvariant()}.png")); WindowAppearance.Capture(main, Path.Combine(Store.Root, $"main-native-{mode.ToLowerInvariant()}.png"));
            main.Width = 900; main.Height = 610; await Task.Delay(250); Capture(main, Path.Combine(Store.Root, $"main-small-{mode.ToLowerInvariant()}.png"));
            main.WindowState = WindowState.Maximized; await Task.Delay(250); Capture(main, Path.Combine(Store.Root, $"main-maximized-{mode.ToLowerInvariant()}.png")); main.WindowState = WindowState.Normal; main.Width = 1040; main.Height = 720;
            var info = new InfoWindow(vm) { Owner = main }; info.Show(); await Task.Delay(200); Capture(info, Path.Combine(Store.Root, $"info-{mode.ToLowerInvariant()}.png")); info.Close();
            var settings = new SettingsWindow(vm); settings.Show();
            foreach (var box in FindAll<System.Windows.Controls.TextBox>(settings)) box.Text = box.Name == "ServerBox" ? "https://music.example.test:4533" : box.Name == "UsernameBox" ? "中文用户 gyjpQ 0123456789" : "";
            foreach (var box in FindAll<System.Windows.Controls.PasswordBox>(settings)) box.Password = "Example-Qgyj-123";
            await Task.Delay(200); Capture(settings, Path.Combine(Store.Root, $"settings-{mode.ToLowerInvariant()}.png")); WindowAppearance.Capture(settings, Path.Combine(Store.Root, $"settings-native-{mode.ToLowerInvariant()}.png"));
            foreach (var box in FindAll<System.Windows.Controls.Control>(settings).Where(c => c is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox))
            {
                var text = new FormattedText("中文 gyjpQ 0123", System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch), box.FontSize, System.Windows.Media.Brushes.Black, VisualTreeHelper.GetDpi(box).PixelsPerDip);
                var host = box.Template.FindName("PART_ContentHost", box) as FrameworkElement;
                checks.Add(new { Theme = mode, Name = box.Name, Height = box.ActualHeight, TextHeight = text.Height, HostHeight = host?.ActualHeight, UsableHeight = (host?.ActualHeight ?? 0) - box.Padding.Top - box.Padding.Bottom, Fits = (host?.ActualHeight ?? 0) - box.Padding.Top - box.Padding.Bottom >= text.Height });
            }
            var scroll = Find<System.Windows.Controls.ScrollViewer>(settings); scroll?.ScrollToBottom(); await Task.Delay(200); Capture(settings, Path.Combine(Store.Root, $"settings-bottom-{mode.ToLowerInvariant()}.png"));
            var combo = Find<System.Windows.Controls.ComboBox>(settings); if (combo != null) { combo.IsDropDownOpen = true; await Task.Delay(150); Capture(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup popup ? (FrameworkElement)popup.Child : combo, Path.Combine(Store.Root, $"theme-menu-{mode.ToLowerInvariant()}.png")); combo.IsDropDownOpen = false; }
            settings.Height = 570; scroll?.ScrollToTop(); await Task.Delay(200); Capture(settings, Path.Combine(Store.Root, $"settings-small-{mode.ToLowerInvariant()}.png")); settings.Close();
            foreach (var list in FindAll<System.Windows.Controls.ListBox>(main).Where(l => l.ContextMenu != null)) { var menu = list.ContextMenu!; menu.PlacementTarget = list; menu.IsOpen = true; await Task.Delay(100); Capture(menu, Path.Combine(Store.Root, $"menu-{list.Name}-{mode.ToLowerInvariant()}.png")); menu.IsOpen = false; }
            app.ShowMini(); await Task.Delay(200); var mini = Application.Current.Windows.Cast<Window>().OfType<MiniPlayerWindow>().First(); Capture(mini, Path.Combine(Store.Root, $"mini-{mode.ToLowerInvariant()}.png")); mini.Hide(); app.CaptureTrayMenu(Path.Combine(Store.Root, $"tray-menu-{mode.ToLowerInvariant()}.png"));
        }
        var savedCover = vm.Cover; vm.Cover = null; taskbar.Refresh(); await Task.Delay(200); taskbar.Capture(Path.Combine(Store.Root, "taskbar-no-cover.png")); vm.Cover = savedCover;
        foreach (var asset in new[] { "chilimusic.png", "player.ico" }) { using var input = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/" + asset)).Stream; using var output = File.Create(Path.Combine(Store.Root, "embedded-" + asset)); input.CopyTo(output); }
        Theme.Apply(vm.Settings.Theme); app.ShowMain(); taskbar.Refresh(); Store.Write("layout-report.json", new { InputChecks = checks, Taskbar = taskbar.Diagnostics(), HitAreas = taskbar.HitDiagnostics(), CompletedUtc = DateTime.UtcNow });
    }
    public static async Task RunAsync(App app, TaskbarService taskbar)
    {
        var vm = app.Vm; var report = new Dictionary<string, object>(); void Record(string name, object value) { report[name] = value; Store.Write("ui-verification.json", report); }
        await vm.ConnectAsync(); Record("connection", "PASS"); var tracks = await vm.Api.RandomAsync(); var chosen = tracks.FirstOrDefault(t => t.Suffix == "flac" && !string.IsNullOrEmpty(t.CoverArt) && t.Artist != "[Unknown Artist]") ?? tracks.First(t => t.Suffix == "flac");
        vm.Queue.Clear(); foreach (var t in tracks.Take(8)) vm.Queue.Add(t); int index = vm.Queue.IndexOf(chosen); if (index < 0) { vm.Queue.Add(chosen); index = vm.Queue.Count - 1; }
        await vm.PlayAsync(index);
        for (int i = 0; i < 60 && vm.Player.Get("audio-codec-name") == ""; i++) await Task.Delay(250);
        await Task.Delay(2000); Record("wasapi", new { Codec = vm.Player.Get("audio-codec-name"), Driver = vm.Player.Get("current-ao"), Duration = vm.Player.Duration, Position = vm.Player.Position });
        Record("taskbar", taskbar.Diagnostics());
        var active = vm.Current; vm.Move(index, 0); bool movePreserves = ReferenceEquals(vm.Current, active); vm.Remove(vm.Queue.Count - 1); Record("queueMoveKeepsPlaying", movePreserves && ReferenceEquals(vm.Current, active)); vm.SaveQueue(); var saved = Store.Read("queue.json", new QueueState()); Record("queueRestore", saved.Tracks.Count == vm.Queue.Count && saved.Index == vm.Index);
        vm.Player.Pause(); var pos = vm.Player.Position; await Task.Delay(700); Record("pauseStable", Math.Abs(pos - vm.Player.Position) < 0.2); vm.Player.Resume(); vm.Seek(12); await Task.Delay(800); Record("seek", Math.Abs(vm.Player.Position - 12) < 3);
        app.ShowMain(); await vm.BrowseAsync("最近添加"); await Task.Delay(5000);
        foreach (var asset in new[] { "chilimusic.png", "player.ico" }) { using var input = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/" + asset)).Stream; using var output = File.Create(Path.Combine(Store.Root, "embedded-" + asset)); input.CopyTo(output); }
        var savedCover = vm.Cover; vm.Cover = null; taskbar.Refresh(); await Task.Delay(200); taskbar.Capture(Path.Combine(Store.Root, "taskbar-no-cover.png")); vm.Cover = savedCover; taskbar.Refresh();
        foreach (var mode in new[] { "Light", "Dark" })
        {
            Theme.Apply(mode); taskbar.Refresh(); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await Task.Delay(300);
            app.CaptureTrayMenu(Path.Combine(Store.Root, $"tray-menu-{mode.ToLowerInvariant()}.png"));
            var main = Application.Current.Windows.Cast<Window>().First(w => w is MainWindow); Capture(main, Path.Combine(Store.Root, $"main-{mode.ToLowerInvariant()}.png")); taskbar.Capture(Path.Combine(Store.Root, $"taskbar-{mode.ToLowerInvariant()}.png"));
            main.Activate(); await Task.Delay(200); WindowAppearance.Capture(main, Path.Combine(Store.Root, $"main-native-{mode.ToLowerInvariant()}.png"));
            var info = new InfoWindow(vm) { Owner = main }; info.Show(); await Task.Delay(200); Capture(info, Path.Combine(Store.Root, $"info-{mode.ToLowerInvariant()}.png")); info.Close();
            var settings = new SettingsWindow(vm); settings.Show(); await Task.Delay(200); Capture(settings, Path.Combine(Store.Root, $"settings-{mode.ToLowerInvariant()}.png"));
            var scroll = Find<System.Windows.Controls.ScrollViewer>(settings); scroll?.ScrollToBottom(); await Task.Delay(200); Capture(settings, Path.Combine(Store.Root, $"settings-bottom-{mode.ToLowerInvariant()}.png"));
            var combo = Find<System.Windows.Controls.ComboBox>(settings); if (combo != null) { combo.IsDropDownOpen = true; await Task.Delay(200); Capture(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup popup ? (FrameworkElement)popup.Child : combo, Path.Combine(Store.Root, $"theme-menu-{mode.ToLowerInvariant()}.png")); combo.IsDropDownOpen = false; }
            settings.Close();
            foreach (var list in FindAll<System.Windows.Controls.ListBox>(main).Where(l => l.ContextMenu != null)) { var menu = list.ContextMenu!; menu.PlacementTarget = list; menu.IsOpen = true; await Task.Delay(200); Capture(menu, Path.Combine(Store.Root, $"menu-{list.Name}-{mode.ToLowerInvariant()}.png")); menu.IsOpen = false; }
            app.ShowMini(); await Task.Delay(400); var mini = Application.Current.Windows.Cast<Window>().First(w => w is MiniPlayerWindow); Capture(mini, Path.Combine(Store.Root, $"mini-{mode.ToLowerInvariant()}.png")); mini.Hide();
        }
        Theme.Apply(vm.Settings.Theme); taskbar.Refresh(); app.ShowMain(); await Task.Delay(500); taskbar.CaptureDesktop(Path.Combine(Store.Root, "taskbar-in-place.png")); Record("taskbarHitAreas", taskbar.HitDiagnostics()); Record("quality", vm.Quality); Record("audioInfo", vm.AudioInfo); Record("screenshots", "PASS"); Record("completedUtc", DateTime.UtcNow.ToString("O"));
    }
    public static async Task RunLocalAsync(App app, string[] files)
    {
        var vm = app.Vm; app.ShowMain(); await Task.Delay(300); var results = new Dictionary<string, object>();
        foreach (var file in files) { await vm.OpenLocalFilesAsync([file]); for (int i = 0; i < 80 && (vm.Player.IsIdle || vm.Player.Get("audio-codec-name") == ""); i++) await Task.Delay(100); await Task.Delay(1000); string codec = vm.Player.Get("audio-codec-name"); vm.Player.Pause(); double pos = vm.Player.Position; await Task.Delay(500); bool paused = Math.Abs(vm.Player.Position - pos) < .2; vm.Player.Resume(); vm.Seek(8); await Task.Delay(500); results[Path.GetExtension(file)] = new { Codec = codec, Duration = vm.Player.Duration, Pause = paused, Seek = Math.Abs(vm.Player.Position - 8) < 2, Local = vm.Current?.IsLocal, Quality = vm.Quality }; Store.Write("local-test.json", results); }
        var main = Application.Current.Windows.Cast<Window>().OfType<MainWindow>().First();
        foreach (string mode in new[] { "Light", "Dark" }) { Theme.Apply(mode); main.Activate(); await Task.Delay(300); Capture(main, Path.Combine(Store.Root, $"local-{mode.ToLowerInvariant()}.png")); WindowAppearance.Capture(main, Path.Combine(Store.Root, $"local-native-{mode.ToLowerInvariant()}.png")); }
        vm.SaveQueue(); results["PersistedLocalQueue"] = Store.Read("queue.json", new QueueState()).Tracks.All(t => t.IsLocal); results["ServerConfigured"] = vm.Api.Configured; Store.Write("local-test.json", results); Theme.Apply(vm.Settings.Theme);
    }
    private static T? Find<T>(DependencyObject root) where T : DependencyObject => FindAll<T>(root).FirstOrDefault();
    internal static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in FindAll<T>(child)) yield return nested; } }
    internal static void Capture(FrameworkElement window, string path)
    {
        window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
