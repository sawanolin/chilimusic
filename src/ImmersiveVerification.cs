using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ChiliMusic;

internal static class ImmersiveVerification
{
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    internal static async Task AnonymousLyricsAsync()
    {
        FeatureVerification.RequireIsolatedProfile(); var settings = Store.Read("config.json", new AppSettings()); using var api = new MusicApiClient(settings, CredentialService.Unprotect(settings.ProtectedPassword));
        void Stage(string stage) => Store.Write("anonymous-lyrics-progress.json", new { Stage = stage });
        Stage("anonymous session");
        Check(!api.Netease.LoggedIn, "Anonymous profile unexpectedly authenticated");
        var candidates = await api.Netease.SearchLyricsAsync("晴天 周杰伦", default);
        Stage("search returned " + candidates.Count);
        var match = candidates.First(t => !string.IsNullOrWhiteSpace(t.Title) && !string.IsNullOrWhiteSpace(t.Artist)); Stage("match chosen");
        var original = new Track { Id = "missing-server-lyrics", Title = match.Title, Artist = match.Artist, Album = match.Album, Duration = match.Duration };
        var lyrics = new LyricsService(api); var docs = await lyrics.LoadAsync(original, default);
        Stage("supplement returned " + docs.Count);
        Check(docs.Any(d => d.Source.StartsWith("网易云") && d.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text))), "Anonymous server lyric fallback");
        Check(!api.Netease.LoggedIn && !File.Exists(Path.Combine(Store.Root, "netease-account.json")), "Lyrics created a login session");
        var applied = await lyrics.ApplyMatchAsync(original, match, default); Check(applied.Count > 0, "Anonymous manual match");
        Store.Write("anonymous-lyrics.json", new { Result = "PASS", AnonymousSearch = true, ServerFallback = true, ManualMatch = true, LoginSaved = false });
    }
    internal static async Task RunAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var report = new Dictionary<string, object>(); ImmersiveWindow? window = null;
        try
        {
            var wave = Path.Combine(Store.Root, "smooth-playback.wav"); FeatureVerification.WriteWave(wave, 120, 0);
            vm.ReplaceQueue([new Track { Id = "local:smooth", LocalPath = wave, Title = "夜航", Artist = "林间", Album = "晚风与海", Duration = 120, Suffix = "wav" }, new Track { Id = "local:second", LocalPath = wave, Title = "播放列表中文文字应完整可见", Artist = "第二位歌手", Album = "专辑", Duration = 120, Suffix = "wav" }]);
            await vm.PlayAsync(0); for (int i = 0; i < 100 && !vm.IsProgressMoving; i++) await Task.Delay(50); Check(vm.IsProgressMoving, "Playback did not start");
            string coverFile = Path.Combine(Store.Root, "reference-cover.png"); if (File.Exists(coverFile)) vm.Cover = CoverCacheService.Load(coverFile);
            string file = Path.Combine(Store.Root, "flow.lrc"); await File.WriteAllTextAsync(file, string.Join('\n', Enumerable.Range(0, 24).Select(i => $"[00:{i * 3:00}.00]" + new[] { "当海风推开夜的门", "我向着未知的海平线", "在星光与浪花之间", "听见心跳的回声" }[i % 4]))); await vm.ImportLyricsAsync(file);
            var originalLines = vm.LyricLines.Select(line => new LyricLine(line.Time, line.Text)).ToList();
            typeof(PlayerViewModel).GetProperty(nameof(vm.LyricDocuments))!.SetValue(vm, new List<LyricsDocument> { new("歌词", originalLines), new("翻译", originalLines.Select(line => new LyricLine(line.Time, "Under the distant sky")).ToList()) });
            vm.SetLyricDocument(0); vm.Changed(nameof(vm.LyricDocuments));
            window = new ImmersiveWindow(vm); window.Show(); app.ShowMain(); foreach (var main in Application.Current.Windows.OfType<MainWindow>()) main.Hide();
            var cases = new List<object>();
            foreach (var palette in ThemeCatalog.All.Where(t => !Environment.GetCommandLineArgs().Contains("--qa-flow-only") || t.Id == "ink_blue"))
            foreach (var size in new[] { (860d, 600d), (1100d, 760d) })
            {
                Theme.Apply(palette.Id); window.Width = size.Item1; window.Height = size.Item2; await Task.Delay(80); window.UpdateLayout();
                var translation = UiVerification.FindAll<ToggleButton>(window).Single(t => Equals(t.Content, "翻译"));
                Check(translation.IsVisible && vm.HasLyricTranslation, "Available translation button hidden"); translation.IsChecked = false; Check(!vm.ShowLyricTranslation, "Translation toggle not bound"); translation.IsChecked = true;
                Check(UiVerification.FindAll<ComboBox>(window).All(box => !box.IsVisible), "Redundant one-language selector visible");
                Check(((Border)window.Content).Child is FrameworkElement { Clip: RectangleGeometry }, "Window corners missing");
                var buttons = UiVerification.FindAll<Button>(window).Where(b => b.IsVisible).ToList();
                foreach (var button in buttons) { var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize)); Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1, "Button outside window"); }
                for (int a = 0; a < buttons.Count; a++) for (int b = a + 1; b < buttons.Count; b++) { var first = buttons[a].TransformToAncestor(window).TransformBounds(new Rect(buttons[a].RenderSize)); var second = buttons[b].TransformToAncestor(window).TransformBounds(new Rect(buttons[b].RenderSize)); var overlap = Rect.Intersect(first, second); Check(overlap.IsEmpty || overlap.Width < 1 || overlap.Height < 1, "Overlapping player buttons"); }
                window.QueuePopup.IsOpen = true; await Task.Delay(80); window.QueueList.UpdateLayout();
                var row = (ListBoxItem)window.QueueList.ItemContainerGenerator.ContainerFromItem(vm.Queue[1]);
                var title = UiVerification.FindAll<TextBlock>(row).First(t => t.Text == vm.Queue[1].Title);
                Check(title.ActualWidth > 20 && title.ActualHeight > 10 && title.FontFamily.Source == window.FontFamily.Source, "Queue title font or size invalid");
                Check(title.Foreground is SolidColorBrush foreground && foreground.Color == ((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color, "Queue title theme contrast invalid");
                UiVerification.Capture(window, Path.Combine(Store.Root, $"immersive-{palette.Id}-{size.Item1}.png")); UiVerification.Capture((FrameworkElement)window.QueuePopup.Child, Path.Combine(Store.Root, $"queue-{palette.Id}-{size.Item1}.png")); window.QueuePopup.IsOpen = false;
                cases.Add(new { Theme = palette.Id, Width = size.Item1, QueueTitleVisible = true, ButtonsInside = true });
                if (size.Item1 == 1100) { var account = new NeteaseAccountWindow(vm, false); account.Show(); await Task.Delay(60); UiVerification.Capture(account, Path.Combine(Store.Root, $"account-{palette.Id}.png")); account.Close(); }
            }
            Theme.Apply("ink_blue"); vm.Seek(12); await Task.Delay(650);
            var scroll = UiVerification.FindAll<ScrollViewer>(window.Lyrics).First(); double initial = scroll.VerticalOffset;
            vm.Seek(24); await Task.Delay(70); double early = scroll.VerticalOffset; await Task.Delay(500); double final = scroll.VerticalOffset;
            report["LyricScroll"] = new { Initial = initial, Early = early, Final = final, AnimationsEnabled = SystemParameters.ClientAreaAnimation };
            Store.Write("flow-progress.json", report);
            Check(!SystemParameters.ClientAreaAnimation || early > initial + 1 && final > early + 1, "Lyric follow jumps instead of animating");
            var samples = new List<double>(); EventHandler collect = (_, _) => samples.Add(window.SeekSlider.Value); CompositionTarget.Rendering += collect; await Task.Delay(800); CompositionTarget.Rendering -= collect;
            int distinct = samples.Distinct().Count(); Check(distinct >= 12 && samples.Zip(samples.Skip(1)).All(pair => pair.Second >= pair.First - .1), "Progress animation not smooth or moves backwards");
            vm.PausePlayback(); await Task.Delay(80); double paused = window.SeekSlider.Value; await Task.Delay(250); Check(Math.Abs(paused - window.SeekSlider.Value) < .03, "Progress moves when paused");
            report["SeekFrames"] = new { Total = samples.Count, Distinct = distinct, PausedStable = true }; report["Themes"] = cases;
            window.SeekSlider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            window.SeekSlider.Value = 36;
            window.SeekSlider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Task.Delay(150); Check(Math.Abs(vm.Position - 36) < .25, "Dragging progress did not seek");
            window.SeekSlider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            window.SeekSlider.Value = 60;
            window.SeekSlider.RaiseEvent(new DragCompletedEventArgs(0, 0, true) { RoutedEvent = Thumb.DragCompletedEvent });
            await Task.Delay(150); Check(Math.Abs(vm.Position - 36) < .25 && Math.Abs(window.SeekSlider.Value - vm.Position) < .25, "Canceled dragging changed playback or display");
            report["ProgressInteraction"] = new { DragSeek = true, CanceledDrag = true };
            await VerifyMixedSampleRatesAsync(); report["MixedRatePlayback"] = "PASS";
            report["Result"] = "PASS";
        }
        catch (Exception error) { report["Result"] = "FAIL"; report["Error"] = error.Message; }
        finally { Store.Write("immersive-verification.json", report); window?.Close(); vm.Stop(); app.Exit(); }
    }
    internal static async Task BestAudioAsync()
    {
        FeatureVerification.RequireIsolatedProfile(); var settings = Store.Read("config.json", new AppSettings()); using var api = new MusicApiClient(settings, CredentialService.Unprotect(settings.ProtectedPassword));
        var candidates = await api.Netease.SearchLyricsAsync("晴天 周杰伦", default); Track? selected = null; string? address = null;
        foreach (var track in candidates.Take(3)) { try { address = await api.Netease.ResolveUrlAsync(track, "auto", false, default); selected = track; break; } catch (ApiException error) when (error.Message.StartsWith("这首") || error.Message.StartsWith("网易云没有提供")) { } }
        Check(selected != null && address != null, "No permitted audio available");
        using var native = new MpvPlayerService(new AppSettings { Volume = 100, ReplayGain = "no" }, true); native.Load(address!);
        var deadline = Stopwatch.StartNew(); while (native.Get("audio-codec-name").Length == 0 && deadline.Elapsed.TotalSeconds < 25) await Task.Delay(50);
        Check(native.Get("audio-codec-name").Length > 0 && native.Duration > 0, "Highest available audio failed to decode");
        Store.Write("best-audio.json", new { Result = "PASS", LoggedIn = api.Netease.LoggedIn, Requested = "auto", Format = selected!.Format, BitRate = selected.BitRate, Preview = selected.Preview, Codec = native.Get("audio-codec-name"), SourceRate = native.Get("audio-params/samplerate"), OutputRate = native.Get("audio-out-params/samplerate"), Resampler = native.Get("audio-swresample-o"), Filters = native.Get("af"), ReplayGain = native.Get("replaygain") });
    }
    private static async Task VerifyMixedSampleRatesAsync()
    {
        void Wave(string file, int rate) { using var writer = new BinaryWriter(File.Create(file)); int bytes = rate * 2 * 2; writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]); }
        string low = Path.Combine(Store.Root, "rate-44100.wav"), high = Path.Combine(Store.Root, "rate-96000.wav"); Wave(low, 44100); Wave(high, 96000);
        using var native = new MpvPlayerService(new AppSettings { Volume = 100, ReplayGain = "no" }, true); native.Load(low); native.PrepareNext(high);
        var deadline = Stopwatch.StartNew(); while (native.Get("playlist-pos") != "1" && deadline.Elapsed.TotalSeconds < 10) await Task.Delay(25);
        Check(native.Get("playlist-pos") == "1", "Mixed-format transition failed"); Check(native.Get("audio-params/samplerate") == "96000", "Second source was not decoded at 96 kHz");
        Check(native.Get("audio-out-params/samplerate") == "96000", "Second output retained first song sample rate");
        Check(native.Get("replaygain") == "no", "Unexpected volume processing");
        Check(native.Get("af") is "" or "[]", "Unexpected audio filters");
    }
}
