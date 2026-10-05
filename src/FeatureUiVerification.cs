using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Control = System.Windows.Controls.Control;
using FlowDirection = System.Windows.FlowDirection;
namespace ChiliMusic;

internal static class FeatureUiVerification
{
    public static async Task RunAsync(App app, TaskbarService taskbar, bool quick = false)
    {
        FeatureVerification.RequireIsolatedProfile();
        var vm = app.Vm; var report = new Dictionary<string, object>(); void Record(string name, object value) { report[name] = value; Store.Write("feature-ui.json", report); }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        void Capture(FrameworkElement window, string name) => UiVerification.Capture(window, Path.Combine(Store.Root, name + ".png"));
        void CheckButtons(FrameworkElement window)
        {
            foreach (var button in UiVerification.FindAll<Button>(window).Where(b => b.IsVisible && b.ActualHeight > 0 && b.Content is string))
            {
                var text = new FormattedText((string)button.Content, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(button).PixelsPerDip);
                Check(button.ActualHeight - button.Padding.Top - button.Padding.Bottom + 1 >= text.Height && button.ActualWidth - button.Padding.Left - button.Padding.Right + 1 >= text.Width, "button label clipped: " + button.Content);
            }
            foreach (var heading in UiVerification.FindAll<TextBlock>(window).Where(t => t.Tag as string == "EmptyHeading" && t.IsVisible))
            {
                var host = VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(heading)) as Grid;
                if (host == null) continue; var rect = heading.TransformToAncestor(host).TransformBounds(new System.Windows.Rect(heading.RenderSize)); Check(rect.Top >= -.5 && rect.Bottom <= host.ActualHeight + .5, "empty-state heading clipped");
            }
        }
        try
        {
            await vm.ConnectAsync(); app.ShowMain(); await vm.BrowseAsync("专辑"); var main = Application.Current.Windows.Cast<Window>().OfType<MainWindow>().First(); await Task.Delay(500);
            int initial = vm.Library.Count; if (vm.HasMore) await vm.LoadMoreAsync(); Record("albumPagination", new { Initial = initial, Total = vm.Library.Count, Unique = vm.Library.Select(a => a.Id).Distinct().Count() });
            Check(vm.Library.Select(a => a.Id).Distinct().Count() == vm.Library.Count, "album page duplicate");
            var list = (ListBox)main.FindName("LibraryList"); var scroll = UiVerification.FindAll<ScrollViewer>(list).First(); scroll.ScrollToVerticalOffset(2000); await Task.Delay(2000);
            Record("albumVirtualization", new { Count = list.Items.Count, Realized = UiVerification.FindAll<ListBoxItem>(list).Count(), Covers = vm.Library.Count(a => a.Cover != null) });
            Check(UiVerification.FindAll<ListBoxItem>(list).Count() < vm.Library.Count || vm.Library.Count < 24, "album virtualization");
            await vm.LoadVisibleCoversAsync(36, 12); Check(vm.Library.Skip(36).Take(12).Any(a => a.Cover != null) || vm.Library.Count < 36, "covers beyond first 16");
            var album = vm.Library.First(a => a.Kind == "album"); await vm.BrowseAsync(album.Title, item: album); string albumName = vm.Section; await vm.BrowseAsync("搜索", album.Title); vm.SearchCategory = "专辑"; await vm.ClearSearchAsync(); Check(vm.Section == albumName && vm.ShowSongs && vm.Results.Count > 0, "search clear restores album"); Record("searchRestore", true); vm.SearchCategory = "全部";
            var track = vm.Results.First(); await vm.PlayTrackAsync(track); await Task.Delay(2500); Check(vm.Player.Get("current-ao") == "wasapi", "real WASAPI"); Record("outputDevices", vm.Player.AudioDevices().Select(d => new { d.Name, d.Description }).ToArray());
            await vm.BrowseAsync("最近添加"); scroll.ScrollToTop(); await Task.Delay(2500);
            var fits = new List<object>();
            foreach (string theme in new[] { "Light", "Dark" })
            {
                Theme.Apply(theme); main.Activate(); await Task.Delay(200); Capture(main, "main-" + theme); WindowAppearance.Capture(main, Path.Combine(Store.Root, "main-native-" + theme + ".png"));
                main.Width = 900; main.Height = 610; await Task.Delay(150); Capture(main, "main-small-" + theme); main.Width = 1040; main.Height = 720;
                CheckButtons(main); main.WindowState = WindowState.Maximized; await Task.Delay(180); Capture(main, "main-maximized-" + theme); Check(((Border)main.Content).CornerRadius.TopLeft == 0, "maximized corners"); main.WindowState = WindowState.Normal; await Task.Delay(120); Check(((Border)main.Content).CornerRadius.TopLeft == 10, "restored corners");
                var settings = new SettingsWindow(vm); settings.Show(); await Task.Delay(200);
                for (int page = 0; page < 7; page++)
                {
                    settings.SelectPage(page); await Task.Delay(120); var settingsScroll = UiVerification.FindAll<ScrollViewer>(settings).First(); Capture(settings, $"settings-{theme}-{page}");
                    CheckButtons(settings); Check(UiVerification.FindAll<TextBlock>(settings).Where(t => t.Text is "常规" or "音频输出" && t.FontWeight == FontWeights.SemiBold).All(t => t.Foreground is SolidColorBrush brush && brush.Color == ((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color), "settings content inherited navigation accent");
                    foreach (var input in UiVerification.FindAll<Control>(settings).Where(c => c is TextBox or PasswordBox))
                    {
                    var host = input.Template.FindName("PART_ContentHost", input) as FrameworkElement; var text = new FormattedText("中文 gyjpQ 0123", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(input).PixelsPerDip);
                    bool fit = (host?.ActualHeight ?? 0) - input.Padding.Top - input.Padding.Bottom >= text.Height; fits.Add(new { Theme = theme, Page = page, input.Name, input.ActualHeight, Host = host?.ActualHeight, Text = text.Height, Fits = fit }); Check(fit, "settings input clipped");
                    }
                    settings.Width = 640; settings.Height = 480; await Task.Delay(100); settingsScroll.ScrollToBottom(); await Task.Delay(100); Capture(settings, $"settings-small-{theme}-{page}"); settings.Width = 760; settings.Height = 620;
                    if (page == 0) { var combo = (ComboBox)settings.FindName("ThemeBox"); combo.IsDropDownOpen = true; await Task.Delay(100); if (combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement popup }) Capture(popup, "theme-menu-" + theme); combo.IsDropDownOpen = false; }
                }
                settings.Close();
                var foreground = ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color; double contrast = new[] { "AccentFillBrush", "AccentHoverBrush", "AccentPressedBrush" }.Min(key => Theme.Contrast(((SolidColorBrush)Application.Current.Resources[key]).Color, foreground)); Check(contrast >= 4.5, "accent button contrast"); Record("buttonContrast-" + theme, contrast);
                var info = new InfoWindow(vm) { Owner = main }; info.Show(); await Task.Delay(100); Capture(info, "info-" + theme); info.Close();
                app.ShowMini(); await Task.Delay(150); var mini = Application.Current.Windows.Cast<Window>().OfType<MiniPlayerWindow>().First(); Capture(mini, "mini-" + theme); mini.Hide();
                app.CaptureTrayMenu(Path.Combine(Store.Root, "tray-menu-" + theme + ".png"));
                foreach (string tab in new[] { "歌词", "歌单", "离线", "定时" }) { var tools = new MusicToolsWindow(vm, tab); tools.Show(); await Task.Delay(400); CheckButtons(tools); Capture(tools, "tools-" + tab + "-" + theme); tools.Width = 600; tools.Height = 460; await Task.Delay(100); CheckButtons(tools); Capture(tools, "tools-small-" + tab + "-" + theme); tools.Close(); }
                foreach (var items in new[] { main.FindName("TracksList"), main.FindName("LibraryList"), main.FindName("QueueList") }.OfType<ListBox>()) { var menu = items.ContextMenu!; menu.PlacementTarget = items; menu.IsOpen = true; await Task.Delay(80); Capture(menu, "menu-" + items.Name + "-" + theme); menu.IsOpen = false; }
                app.ToggleDesktopLyrics(); await Task.Delay(150); var desktop = Application.Current.Windows.Cast<Window>().OfType<DesktopLyricsWindow>().First(); Capture(desktop, "desktop-lyrics-" + theme); desktop.ToggleLock(); Check(desktop.Locked, "desktop lock"); desktop.Unlock(); desktop.Close();
                foreach (int width in new[] { 240, 316, 480 }) { vm.Settings.TaskbarWidth = width; vm.Settings.TaskbarShowArtist = width != 240; vm.Settings.TaskbarShowCover = width != 480; taskbar.Refresh(); await Task.Delay(100); taskbar.Capture(Path.Combine(Store.Root, $"taskbar-{theme}-{width}.png")); Record($"taskbar-{theme}-{width}", new { Geometry = taskbar.Diagnostics(), Hits = taskbar.HitDiagnostics() }); }
            }
            Record("settingsInputs", fits); vm.Settings.TaskbarWidth = 316; vm.Settings.TaskbarShowArtist = vm.Settings.TaskbarShowCover = true; Theme.Apply(vm.Settings.Theme); taskbar.Refresh();
            await vm.BrowseAsync("本地音乐"); Capture(main, "main-empty-local"); Check(((FrameworkElement)main.FindName("EmptyPanel")).ActualHeight > 40, "empty local page has no layout space"); await vm.BrowseAsync("搜索", "__chilimusic_empty_search__"); Capture(main, "main-empty-search"); Check(((FrameworkElement)main.FindName("EmptyPanel")).ActualHeight > 40, "empty search page has no layout space"); await vm.ClearSearchAsync();
            Record("windowLayoutChecks", true);
            vm.ReplaceQueue(Enumerable.Range(0, 10000).Select(i => new Track { Id = "queue-test:" + i, Title = "歌曲 " + i })); Check(vm.Queue.Count == 10000 && vm.Index == -1, "bulk queue replace"); vm.ClearQueue(); Record("largeQueueReplace", true);
            var lyricFixture = Path.Combine(Store.Root, "lyric.wav"); FeatureVerification.WriteWave(lyricFixture, 8, 0); await File.WriteAllTextAsync(Path.ChangeExtension(lyricFixture, ".lrc"), "[00:00.00]第一行\n[00:01.00]第二行\n[00:03.00]第三行"); await vm.OpenLocalFilesAsync([lyricFixture]); await Task.Delay(800); vm.Seek(1.2); await Task.Delay(500); Check(vm.LyricText == "第二行", "synced lyric position"); vm.LyricOffset = 2; await Task.Delay(300); Check(vm.LyricText == "第三行", "lyric offset"); vm.LyricOffset = 0;
            foreach (string mode in new[] { "Light", "Dark" }) { Theme.Apply(mode); var lyricsWindow = new MusicToolsWindow(vm, "歌词"); lyricsWindow.Show(); await Task.Delay(100); Capture(lyricsWindow, "synced-lyrics-" + mode); lyricsWindow.Close(); }
            Record("syncedLyricsAndOffset", true);
            var fixture = Path.Combine(Store.Root, "sleep.wav"); FeatureVerification.WriteWave(fixture, 3, 220); var local = LocalLibraryService.ReadTrack(fixture); vm.Stop(); vm.Queue.Clear(); vm.Queue.Add(local); vm.Queue.Add(local); await vm.PlayAsync(0); await Task.Delay(300); vm.SetSleep(0, true); await Task.Delay(4000); Check(vm.Player.IsIdle && vm.Index == 0, "finish current leaked next"); Record("sleepFinishCurrent", true); vm.CancelSleep();
            if (!quick) { var timerFixture = Path.Combine(Store.Root, "timer.wav"); FeatureVerification.WriteWave(timerFixture, 65, 0); vm.Stop(); vm.Queue.Clear(); vm.Queue.Add(LocalLibraryService.ReadTrack(timerFixture)); await vm.PlayAsync(0); await Task.Delay(300); vm.SetSleep(1, false); Record("sleepDeadlineRunning", DateTime.UtcNow); await Task.Delay(61500); Check(vm.Player.IsIdle && vm.SleepStatus == "未设置", "sleep deadline did not stop"); Record("sleepDeadline", true); }
            vm.Settings.AudioDevice = "wasapi/{00000000-0000-0000-0000-000000000000}"; vm.Player.Set("audio-device", vm.Settings.AudioDevice); await vm.PlayTrackAsync(track); var fallbackDeadline = DateTime.UtcNow.AddSeconds(20); while ((vm.Settings.AudioDevice != "auto" || vm.Player.Get("audio-codec-name") == "") && DateTime.UtcNow < fallbackDeadline) await Task.Delay(100); Check(vm.Settings.AudioDevice == "auto" && vm.Player.Get("audio-codec-name") != "", "output fallback"); Record("deviceFallback", true);
            vm.Player.Pause(); double pausedPosition = vm.Player.Position; var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(vm.Settings))!; copy.ReplayGain = "album"; copy.ReplayGainPreamp = 1; vm.UpdateSettings(copy, vm.Password); await Task.Delay(1000); Check(vm.Player.IsPaused && Math.Abs(vm.Player.Position - pausedPosition) < 1 && vm.Player.Get("replaygain") == "album", "settings interrupted resume"); Record("settingsResume", true); vm.Settings.ReplayGain = "no"; vm.Settings.ReplayGainPreamp = 0; vm.Player.Set("replaygain", "no"); vm.Player.Set("replaygain-preamp", "0");
            vm.Stop(); vm.Queue.Clear(); await vm.PlayTrackAsync(track); await vm.BrowseAsync("最近添加"); app.ShowMain(); await Task.Delay(1800); Capture(main, "main-server"); taskbar.CaptureDesktop(Path.Combine(Store.Root, "taskbar-playing-full.png"));
            Record("complete", DateTime.UtcNow);
        }
        catch (Exception e) { Record("failure", new { Type = e.GetType().Name, Message = e.Message, e.StackTrace }); vm.Status = "功能检查未完成"; }
    }
}
