using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ChiliMusic;

internal static class UsabilityVerification
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task RunAsync(App app, TaskbarService taskbar)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var passed = new List<string>(); var layouts = new List<object>(); bool live = false;
        try
        {
            await vm.LocalLibrary.InitializeAsync();
            string wave = Path.Combine(Store.Root, "silent.wav"); FeatureVerification.WriteWave(wave, 120, 0);
            var album = Enumerable.Range(1, 73).Select(i => new Track { Id = "local:album-" + i, LocalPath = wave, Title = "夜航 " + i, Artist = "歌手", Album = "专辑 A", Duration = 120, Suffix = "wav", DiscNumber = (uint)(i <= 40 ? 1 : 2), TrackNumber = (uint)(i <= 40 ? i : i - 40) }).ToList();
            var other = new Track { Id = "local:other", LocalPath = wave, Title = "另一张专辑", Artist = "其他歌手", Album = "专辑 B", Duration = 120, Suffix = "wav" };
            var local = (List<Track>)typeof(PlayerViewModel).GetField("_localTracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            local.Clear(); local.AddRange(album.AsEnumerable().Reverse()); local.Add(other);
            vm.Settings.Mode = PlayMode.Sequential; await vm.SetMobileOutputAsync(true); vm.ReplaceQueue([other]);
            await vm.BrowseAsync("专辑 A", item: new LibraryItem("local-album:A", "专辑 A", "歌手", "local-album"));
            Check(vm.Results.Count == 73 && vm.Results[0].TrackNumber == 1 && vm.Results[40].DiscNumber == 2, "Album truncated or out of order");
            await vm.PlayBrowseTrackAsync(album[42]); Check(vm.Queue.Count == 73 && vm.Index == 42, "Album context not queued");
            await vm.NextAsync(false); Check(vm.Current == album[43], "Album next skipped source order");
            await vm.PreviousAsync(); Check(vm.Current == album[42], "Album previous failed");
            vm.UpdateMobilePosition(12); await vm.PlayBrowseTrackAsync(album[42]); Check(vm.Queue.Count == 73 && vm.Position == 12, "Repeated play duplicated or restarted current song");
            await vm.BrowseAsync("专辑 B", item: new LibraryItem("local-album:B", "专辑 B", "其他歌手", "local-album")); Check(vm.Current == album[42] && vm.Queue.Count == 73, "Browsing changed playback");
            await vm.GoBackAsync(); Check(vm.Section == "专辑 A" && vm.Results.Count == 73, "Album back navigation failed"); passed.Add("73-track multi-disc album, next/previous, repeat click and browse independence");
            await vm.BrowseAsync("搜索", "夜航"); vm.ArtistFilter = "不存在"; Check(System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Results).IsEmpty, "Filter not applied");
            await vm.ClearSearchAsync(); Check(vm.Section == "专辑 A" && vm.ArtistFilter == "", "Search restore failed");
            vm.AddRange([other, album[0]], true); Check(vm.Queue[43] == other && vm.Queue[44] == album[0], "Batch play-next reversed selection");
            Check(vm.UpcomingIndices().Take(2).SequenceEqual(new[] { 43, 44 }), "Upcoming preview ignored priority");
            vm.Settings.Mode = PlayMode.Shuffle; Check(vm.UpcomingIndices().Take(2).SequenceEqual(new[] { 43, 44 }), "Shuffle preview changed priority"); await vm.NextAsync(false); Check(vm.Current == other, "Play next ignored in shuffle"); await vm.NextAsync(false); Check(vm.Current == album[0], "Second priority track lost"); vm.Settings.Mode = PlayMode.Sequential;
            var order = new PlaybackOrder(); order.Reset(5); order.Record(0); order.QueueNext([3, 4]); var restoredOrder = new PlaybackOrder(); restoredOrder.Restore(5, new QueueState { PlayNext = order.Priority.ToList(), History = order.History.ToList(), HistoryPosition = order.Cursor }); Check(restoredOrder.Next(0, PlayMode.Shuffle, true) == 3 && restoredOrder.Next(3, PlayMode.Shuffle, true) == 4, "Priority lost on restart");
            int before = vm.Queue.Count; await vm.PlayTrackAsync(other); await vm.PlayTrackAsync(other); Check(vm.Queue.Count == before, "Single track duplicate");
            vm.ReplaceQueue([album[0], album[1], album[0]]); await vm.PlayAsync(2); vm.Move(0, 1); Check(vm.Index == 2, "Moving duplicate jumped current entry"); vm.Remove(1); Check(vm.Index == 1 && vm.Current == album[0], "Removing duplicate jumped current entry");
            await vm.PlayFromListAsync(album[1], album.Take(3)); vm.Remove(1); await Task.Delay(50); Check(vm.Current == album[2] && vm.MobilePlaying, "Removing playing song stopped continuation");
            vm.PausePlayback(); vm.Remove(vm.Index); await Task.Delay(50); Check(!vm.MobilePlaying, "Removing paused song started playback");
            vm.ClearQueue(); Check(vm.Current == null && vm.Duration == 0 && vm.LyricLines.Count == 0, "Clearing queue left stale display"); passed.Add("Search restore, ordered multi-selection, queue reuse, remove and clear");
            await VerifyRemoteAsync(app, vm, album.Take(3).ToList()); passed.Add("Paired phone list playback, next track, paging context and invalid context rejection");
            await vm.SetMobileOutputAsync(false); vm.Settings.Mode = PlayMode.RepeatOne; await vm.PlayFromListAsync(album[0], album.Take(3)); await Task.Delay(500); vm.Add(other, true); vm.Seek(119.75);
            for (int i = 0; i < 50 && vm.Current != other; i++) await Task.Delay(100);
            Check(vm.Current == other, "Native automatic next ignored priority in repeat-one"); vm.PausePlayback(); vm.Settings.Mode = PlayMode.Sequential; passed.Add("Native preloaded play-next overrides repeat-one at song end");
            await vm.SetMobileOutputAsync(false); await vm.PlayFromListAsync(album[1], album.Take(3)); await Task.Delay(250); vm.PausePlayback();
            string lrc = Path.Combine(Store.Root, "lyrics.lrc"); await File.WriteAllTextAsync(lrc, "[00:00.00]第一行歌词\n[00:01.00]第二行歌词\n[00:03.00]第三行歌词"); await vm.ImportLyricsAsync(lrc);
            app.ShowMain(); var main = Application.Current.Windows.OfType<MainWindow>().First();
            await vm.BrowseAsync("专辑 A", item: new LibraryItem("local-album:A", "专辑 A", "歌手", "local-album"));
            await VerifyInteractionsAsync(main, vm, album); passed.Add("Rendered list play, right-click multi-selection, play-next menu and queue location");
            await VerifyNumberedListsAsync(main, vm, album); passed.Add("Row numbers follow filtered views, recycled rows and duplicate queue edits");
            await VerifyTaskbarProgressAsync(taskbar, vm); passed.Add("Taskbar progress reaches the full right edge at all configured widths");
            await vm.ImportLyricsAsync(lrc);
            foreach (var palette in ThemeCatalog.All)
            {
                Theme.Apply(palette.Id);
                foreach (var size in new[] { (900d, 610d), (1040d, 720d) }) { main.Width = size.Item1; main.Height = size.Item2; await InspectAsync(main, "main-" + size.Item1, palette.Id, layouts); }
                var settings = new SettingsWindow(vm); settings.Show();
                for (int i = 0; i < 9; i++) { settings.SelectPage(i); await InspectAsync(settings, "settings-" + i, palette.Id, layouts); }
                settings.Close();
                foreach (string tab in new[] { "歌词", "歌单", "离线", "定时" }) { var tools = new MusicToolsWindow(vm, tab); tools.Show(); await InspectAsync(tools, "tools-" + tab, palette.Id, layouts); tools.Close(); }
                foreach (var entry in new (string Name, Func<Window> Create)[] { ("mini", () => new MiniPlayerWindow(vm)), ("desktop", () => new DesktopLyricsWindow(vm)), ("info", () => new InfoWindow(vm)), ("remote", () => new RemoteControlWindow(app.Remote)), ("account", () => new NeteaseAccountWindow(vm, false)), ("immersive", () => new ImmersiveWindow(vm)) })
                { var window = entry.Create(); window.Show(); await InspectAsync(window, entry.Name, palette.Id, layouts); if (window is ImmersiveWindow immersive) { immersive.QueuePopup.IsOpen = true; await Task.Delay(50); immersive.QueueList.UpdateLayout(); foreach (var row in UiVerification.FindAll<ListBoxItem>(immersive.QueueList)) { int index = immersive.QueueList.ItemContainerGenerator.IndexFromContainer(row); if (index >= 0) Check(UiVerification.FindAll<TextBlock>(row).Single(t => t.Name == "RowNumber").Text == (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), "Immersive queue number incorrect"); } if (palette.Id is "moon_white" or "ink_blue") UiVerification.Capture((FrameworkElement)immersive.QueuePopup.Child, Path.Combine(Store.Root, "immersive-queue-" + palette.Id + ".png")); immersive.QueuePopup.IsOpen = false; } window.Close(); }
            }
            passed.Add("All 13 themes, main sizes, settings pages, tool tabs and auxiliary windows");
            Store.Write("usability-verification.json", new { Result = "PASS", Passed = passed, Layouts = layouts });
            if (Environment.GetCommandLineArgs().Contains("--qa-phone-live")) { await PreparePhoneAsync(app); live = true; }
        }
        catch (Exception error) { Store.Write("usability-verification.json", new { Result = "FAIL", Error = error.Message, error.StackTrace, Passed = passed, Layouts = layouts }); }
        finally { if (!live) { vm.Stop(); app.Exit(); } }
    }
    private static async Task VerifyInteractionsAsync(MainWindow main, PlayerViewModel vm, List<Track> album)
    {
        var songs = (ListBox)main.FindName("TracksList"); var queue = (ListBox)main.FindName("QueueList");
        songs.SelectedItem = album[3]; songs.Focus();
        songs.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        await Task.Delay(250); Check(vm.Queue.Count == 73 && vm.Index == 3, "List Enter lost album context"); vm.PausePlayback();
        songs.SelectedItems.Clear(); songs.SelectedItems.Add(album[5]); songs.SelectedItems.Add(album[4]); main.UpdateLayout();
        var row = (ListBoxItem)songs.ItemContainerGenerator.ContainerFromItem(album[4]);
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Check(songs.SelectedItems.Count == 2, "Right click cleared selected songs");
        songs.ContextMenu!.PlacementTarget = songs; songs.ContextMenu.IsOpen = true; await Task.Delay(35);
        ((MenuItem)songs.ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); songs.ContextMenu.IsOpen = false;
        Check(vm.Queue[4] == album[4] && vm.Queue[5] == album[5], "Menu play-next reordered selected songs");
        ((Button)main.FindName("QueueFollowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(queue.SelectedIndex == vm.Index, "Queue locate failed");
        await vm.PlayFromListAsync(album[1], album.Take(3)); await Task.Delay(100); vm.PausePlayback();
    }
    private static async Task VerifyNumberedListsAsync(MainWindow main, PlayerViewModel vm, List<Track> album)
    {
        var songs = (ListBox)main.FindName("TracksList"); var queue = (ListBox)main.FindName("QueueList");
        void Inspect(ListBox list)
        {
            list.UpdateLayout(); int count = 0;
            foreach (var row in UiVerification.FindAll<ListBoxItem>(list))
            {
                int index = list.ItemContainerGenerator.IndexFromContainer(row); if (index < 0) continue;
                var number = UiVerification.FindAll<TextBlock>(row).Single(t => t.Name == "RowNumber");
                Check(number.Text == (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), $"Incorrect row number after list change: {list.Name}, index={index}, actual={number.Text}, alternation={ItemsControl.GetAlternationIndex(row)}");
                Check(number.ActualWidth >= 24 && number.IsVisible, "Row number missing or clipped"); count++;
            }
            Check(count > 0, "No rendered numbered rows");
        }
        songs.ScrollIntoView(album[^1]); await Task.Delay(50); Inspect(songs);
        Check(songs.ItemContainerGenerator.ContainerFromIndex(72) != null, "Last numbered song not realized");
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Results); var filter = view.Filter;
        try { view.Filter = value => value is Track track && track.TrackNumber % 2 == 0; songs.ScrollIntoView(songs.Items[0]); await Task.Delay(50); Inspect(songs); }
        finally { view.Filter = filter; }
        songs.ScrollIntoView(album[0]); await Task.Delay(50); Inspect(songs);
        vm.ReplaceQueue([album[0], album[0], album[1]]); await Task.Delay(50); Inspect(queue);
        vm.Move(2, 0); await Task.Delay(50); Inspect(queue); vm.Remove(1); await Task.Delay(50); Inspect(queue);
        await vm.PlayFromListAsync(album[1], album.Take(3)); await Task.Delay(100); vm.PausePlayback();
    }
    private static async Task VerifyTaskbarProgressAsync(TaskbarService taskbar, PlayerViewModel vm)
    {
        int width = vm.Settings.TaskbarWidth; bool enabled = vm.Settings.TaskbarEnabled;
        await vm.SetMobileOutputAsync(true); vm.PausePlayback(); vm.Settings.TaskbarEnabled = true;
        try
        {
            foreach (int size in new[] { 240, 316, 480 })
            {
                vm.Settings.TaskbarWidth = size;
                foreach (double fraction in new[] { .5, 1d })
                {
                    vm.UpdateMobilePosition(vm.Duration * fraction); taskbar.Refresh(); await Task.Delay(50);
                    string path = Path.Combine(Store.Root, $"taskbar-progress-{size}-{fraction * 100:0}.png"); taskbar.Capture(path);
                    using var image = new System.Drawing.Bitmap(path);
                    bool reachesEdge = Enumerable.Range(Math.Max(0, image.Height - 12), Math.Min(12, image.Height)).Any(y => { var pixel = image.GetPixel(image.Width - 1, y); return pixel.R + pixel.G + pixel.B > 30; });
                    Check(reachesEdge == (fraction == 1), "Taskbar progress right edge is wrong");
                }
            }
        }
        finally { vm.Settings.TaskbarWidth = width; vm.Settings.TaskbarEnabled = enabled; vm.UpdateMobilePosition(0); taskbar.Refresh(); await vm.SetMobileOutputAsync(false); vm.PausePlayback(); }
    }
    private static async Task PreparePhoneAsync(App app)
    {
        var vm = app.Vm; vm.Stop(); string folder = Path.Combine(Store.Root, "phone-fixtures"); Directory.CreateDirectory(folder);
        byte[] pixels = new byte[128 * 128 * 4]; for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { int offset = (y * 128 + x) * 4; pixels[offset] = (byte)(110 + x); pixels[offset + 1] = (byte)(55 + y); pixels[offset + 2] = 30; pixels[offset + 3] = 255; }
        var bitmap = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, pixels, 128 * 4); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(folder, "cover.png"))) encoder.Save(stream);
        var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(folder, "夜航 " + i + ".wav")).ToArray();
        for (int i = 0; i < paths.Length; i++) { FeatureVerification.WriteWave(paths[i], 20 + i, 0); using var file = TagLib.File.Create(paths[i]); file.Tag.Title = "夜航 " + (i + 1); file.Tag.Performers = ["歌手"]; file.Tag.Album = "专辑 A"; file.Tag.Track = (uint)(i + 1); file.Save(); }
        await vm.LocalLibrary.ImportAsync(paths); var tracks = vm.LocalLibrary.Snapshot.Where(t => paths.Contains(t.LocalPath)).OrderBy(t => t.TrackNumber).ToList(); await vm.PlayFromListAsync(tracks[0], tracks); await Task.Delay(150); vm.PausePlayback();
        app.Remote.Start(47834); Store.Write("phone-live.json", new { Port = app.Remote.Port, Code = app.Remote.PairCode });
        foreach (var main in Application.Current.Windows.OfType<MainWindow>()) main.Hide();
    }
    private static async Task InspectAsync(Window window, string name, string theme, List<object> layouts)
    {
        await Task.Delay(35); window.UpdateLayout();
        if (window is not DesktopLyricsWindow) Check(window.Content is Border { Child: FrameworkElement { Clip: RectangleGeometry } }, "Missing corner clip: " + name);
        foreach (var input in UiVerification.FindAll<TextBox>(window).Where(t => t.IsVisible && t.ActualWidth > 0 && t.ActualHeight > 0))
        {
            var words = new FormattedText("中文 Ag", System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(input).PixelsPerDip);
            Check(input.ActualHeight + 1 >= words.Height + input.Padding.Top + input.Padding.Bottom, "Input text clipped: " + name + "/" + input.Name);
        }
        if (theme is "moon_white" or "ink_blue") UiVerification.Capture(window, Path.Combine(Store.Root, name + "-" + theme + ".png"));
        layouts.Add(new { Window = name, Theme = theme, Corners = true, Inputs = true });
        Store.Write("usability-progress.json", new { Count = layouts.Count, Last = name, Theme = theme });
    }
    private static async Task VerifyRemoteAsync(App app, PlayerViewModel vm, List<Track> tracks)
    {
        app.Remote.Start(47834); string origin = "http://127.0.0.1:47834";
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(origin) };
        async Task<JsonElement> Call(string path, object? body = null, bool expectSuccess = true)
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
            request.Headers.Add("Origin", origin);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request); Check(response.IsSuccessStatusCode == expectSuccess, "Remote request failed: " + path);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        var paired = await Call("/api/pair", new { code = app.Remote.PairCode }); http.DefaultRequestHeaders.Add("X-Chili-Token", paired.GetProperty("token").GetString());
        var songMethod = typeof(RemoteControlService).GetMethod("Song", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var contextMethod = typeof(RemoteControlService).GetMethod("Context", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var row = JsonSerializer.SerializeToElement(songMethod.Invoke(app.Remote, [tracks[1], null]));
        string context = (string)contextMethod.Invoke(app.Remote, [tracks, null])!;
        vm.ReplaceQueue([tracks[0]]);
        await Call("/api/command", new { action = "play-track", key = row.GetProperty("key").GetString(), context }); Check(vm.Index == 1 && vm.Queue.Count == 3, "Phone lost album context");
        await Call("/api/command", new { action = "next" }); Check(vm.Current == tracks[2], "Phone next wrong");
        await Call("/api/command", new { action = "play-track", key = row.GetProperty("key").GetString(), context = "invalid-context" }, false);
        string expanded = (string)contextMethod.Invoke(app.Remote, [tracks, context])!;
        Check(((Dictionary<string, List<Track>>)typeof(RemoteControlService).GetField("_contexts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app.Remote)!)[expanded].Count == 3, "Paging duplicated tracks");
        app.Remote.Stop();
    }
}
