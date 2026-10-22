using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChiliMusic;

internal static class ProductVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task RunAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var passed = new List<string>(); var layouts = new List<object>();
        void Passed(string name) { passed.Add(name); Store.Write("product-progress.json", new { Passed = passed }); }
        try
        {
            await DownloadVerification.RunAsync(); Passed("下载暂停、HTTP Range 续传校验、追加任务、逐项失败与保留策略");
            await vm.LocalLibrary.InitializeAsync();
            string music = Path.Combine(Store.Root, "fixtures"); Directory.CreateDirectory(music);
            string wave = Path.Combine(music, "专辑.wav"); FeatureVerification.WriteWave(wave, 12, 220);
            TagEditorWindow.Write(wave, new Dictionary<string, string> { ["标题"] = "夜航", ["歌手"] = "周杰伦", ["专辑"] = "精选集", ["专辑艺术家"] = "周杰伦", ["年份"] = "2026", ["曲目号"] = "1", ["碟片号"] = "1" }, null);
            var tagged = LocalLibraryService.ReadTrack(wave); Check(tagged.Title == "夜航" && tagged.AlbumArtist == "周杰伦" && tagged.Year == 2026 && tagged.TrackNumber == 1, "Tag write/read mismatch");
            Check(SearchText.Matches("周杰伦 夜航", "zhoujielun") && SearchText.Matches("周杰伦 夜航", "zjl"), "Pinyin search failed"); Passed("本地标签实际写入与中文全拼、首字母检索");
            var legacy = JsonSerializer.Deserialize<Track>(JsonSerializer.Serialize(tagged))!; legacy.Id = "local:preserved-identity"; legacy.AlbumArtist = ""; legacy.Edition = "旧版备注"; legacy.Starred = "favorite";
            Store.Write("local-library.json", new[] { legacy }); Store.Write("local-library-schema.json", 0);
            using (var migrated = new LocalLibraryService(vm.Settings))
            {
                await migrated.InitializeAsync(); var restored = migrated.Snapshot.Single();
                Check(restored.Id == legacy.Id && restored.Starred == "favorite" && restored.AlbumArtist == "周杰伦" && restored.Edition == "", "Existing library metadata migration lost identity or failed to refresh tags");
                TagEditorWindow.Write(wave, new Dictionary<string, string> { ["标题"] = "已重新定位的歌曲" }, null); await migrated.ImportAsync([wave]);
                Check(migrated.Snapshot.Single().Id == legacy.Id && migrated.Snapshot.Single().Title == "已重新定位的歌曲", "Relocated file metadata refresh created duplicate or stale record");
            }
            TagEditorWindow.Write(wave, new Dictionary<string, string> { ["标题"] = "夜航" }, null); await vm.LocalLibrary.PersistAsync(); Passed("旧曲库补读专辑标签并保留收藏与身份，重新定位后的文件可正常更新标签");
            string cue = Path.Combine(music, "album.cue"); await File.WriteAllTextAsync(cue, "PERFORMER \"周杰伦\"\nTITLE \"现场专辑\"\nFILE \"专辑.wav\" WAVE\nTRACK 01 AUDIO\nTITLE \"第一首\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"第二首\"\nINDEX 01 00:05:00\n");
            var segments = CueSheet.Read(cue); Check(segments.Count == 2 && segments[0].Duration == 5 && segments[1].StartOffset == 5 && segments[1].Duration == 7, "CUE segments incorrect");
            await vm.LocalLibrary.ImportAsync([wave, cue]); Check(vm.LocalLibrary.Snapshot.Count(t => t.CuePath == cue) == 2, "CUE not imported as two tracks"); Passed("CUE 分轨、曲目数据与实际文件导入");
            await vm.OpenLocalFilesAsync([music], true); Check(vm.Queue.Count == 2 && vm.Queue.All(t => t.CuePath != null) && vm.Player.IsIdle, "Folder append did not preserve CUE segments or idle state"); vm.ClearQueue(); Passed("文件夹外部打开与追加队列不自动播放");
            vm.LocalLibrary.RemoveFolder(music);
            var local = (List<Track>)typeof(PlayerViewModel).GetField("_localTracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            local.Clear(); local.AddRange(Enumerable.Range(1, 145).Select(i => new Track { Id = "local:test-" + i, LocalPath = wave, Title = "夜航 " + i, Artist = i <= 70 ? "歌手甲" : "歌手乙", AlbumArtist = i <= 70 ? "歌手甲" : "歌手乙", Album = "精选集", Duration = 12, TrackNumber = (uint)i, Suffix = i == 145 ? "flac" : "wav", Year = 2026 }));
            vm.Settings.CatalogSource = "local"; vm.Settings.LocalView = "歌曲"; await vm.SetMobileOutputAsync(true); await vm.BrowseAsync("本地音乐"); Check(vm.Results.Count == 60 && vm.HasMore, "Pagination fixture failed");
            await vm.PlayCompleteBrowseAsync(vm.Results[30]); Check(vm.Queue.Count == 145 && vm.Index == 30, "Complete list playback truncated"); Passed("145 首分页曲库无需滚动即可完整播放");
            await vm.BrowseAsync("本地音乐"); vm.FormatFilter = "FLAC"; for (int i = 0; i < 50 && vm.HasMore; i++) await Task.Delay(30); Check(!vm.HasMore && System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Results).Cast<Track>().Single().Id == "local:test-145", "Filter missed unloaded last page"); Passed("筛选能找到尚未加载页中的歌曲");
            vm.Settings.LocalView = "专辑"; await vm.BrowseAsync("本地音乐"); Check(vm.Library.Count == 2, "Different album artists merged"); Passed("同名专辑按专辑艺术家正确分组");
            vm.Settings.LocalView = "歌曲"; await vm.BrowseAsync("本地音乐"); vm.ReplaceQueue(local.Take(5)); await vm.PlayAsync(1); vm.UpdateMobilePosition(3); vm.ClearQueue(); await vm.UndoQueueAsync(); Check(vm.Queue.Count == 5 && vm.Index == 1 && Math.Abs(vm.Position - 3) < .1, "Undo queue failed"); Passed("清空队列撤销恢复曲目、播放位置与状态");
            var list = vm.Playlists.Create("产品验收歌单", [local[0]]); await vm.AddToPlaylistAsync(list.Id, false, [local[0], local[1]]); Check(list.Tracks.Count == 2, "Duplicate playlist entries not filtered"); Passed("本地歌单添加与重复歌曲处理");
            await vm.SetMobileOutputAsync(false); await vm.PlayFromListAsync(segments[1], segments); await Task.Delay(600); vm.PausePlayback(); Check(vm.Player.Position >= 5 && vm.Duration <= 7.1, "Native CUE start/end incorrect");
            var before = vm.Player.Get("playlist/0/id"); Check(before.Length > 0, "Native playlist identity unavailable"); var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(vm.Settings))!; copy.Theme = "ink_blue"; vm.UpdateSettings(copy, vm.Password); await Task.Delay(150); Check(vm.Player.Get("playlist/0/id") == before && vm.Player.IsPaused, "Appearance setting reloaded playback"); Passed("原生 CUE 定位及主题设置不重载暂停中的音频");
            vm.Settings.EqualizerEnabled = true; vm.Settings.Equalizer = [3,2,1,0,0,0,0,1,2,3]; vm.ApplyAudioProcessing(); Check(vm.Player.Get("af").Contains("equalizer"), "Native equalizer not applied"); vm.Settings.EqualizerEnabled = false; vm.ApplyAudioProcessing(); Passed("十段均衡器及限幅滤镜接入原生音频引擎");
            var recover = typeof(PlayerViewModel).GetMethod("RecoverDeviceOrNetworkAsync", BindingFlags.NonPublic | BindingFlags.Instance)!; double savedPosition = vm.Position; vm.Settings.AudioDevice = "wasapi/missing-fixture"; vm.Settings.DeviceDisconnectPause = false; await (Task)recover.Invoke(vm, null)!; await Task.Delay(180); Check(vm.Settings.AudioDevice == "auto" && vm.Player.IsPaused && Math.Abs(vm.Player.Position - segments[1].StartOffset - savedPosition) < .6, "Device fallback lost position/pause");
            vm.Settings.AudioDevice = "wasapi/missing-fixture"; vm.Settings.DeviceDisconnectPause = true; await (Task)recover.Invoke(vm, null)!; Check(vm.Player.IsPaused && vm.PlaybackFailure.Length > 0, "Device wait policy did not pause"); vm.Settings.AudioDevice = "auto"; vm.Settings.DeviceDisconnectPause = false; vm.ClearPlaybackFailure(); Passed("设备失效路径保留位置和暂停状态，等待设备策略可用");
            Check(vm.ListeningHistory.Count > 0, "Listening history missing"); Passed("本机播放历史记录");
            app.ShowMain(); var main = Application.Current.Windows.OfType<MainWindow>().First();
            await vm.BrowseAsync("本地音乐"); await vm.LoadMoreAsync();
            var detailedList = (ListView)typeof(MainWindow).GetField("_details", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            await Task.Delay(140);
            await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var selectedTrack = vm.Results[90]; detailedList.SelectedItem = selectedTrack; detailedList.ScrollIntoView(selectedTrack); detailedList.UpdateLayout(); await Task.Delay(100);
            var detailedScroll = UiVerification.FindAll<ScrollViewer>(detailedList).First(); double detailedOffset = detailedScroll.VerticalOffset;
            if (detailedOffset <= 0) { UiVerification.Capture(main, Path.Combine(Store.Root, "detailed-scroll-failure.png")); Store.Write("detailed-scroll-failure.json", new { detailedList.IsVisible, detailedList.ActualHeight, detailedList.Items.Count, Selected = detailedList.SelectedIndex, Scrolls = UiVerification.FindAll<ScrollViewer>(detailedList).Select(s => new { s.Name, s.ExtentHeight, s.ViewportHeight, s.VerticalOffset }).ToArray() }); }
            Check(detailedOffset > 0, "Detailed list did not scroll");
            await vm.BrowseAsync("收藏"); await vm.GoBackAsync(); await Task.Delay(140); main.UpdateLayout();
            Check(ReferenceEquals(detailedList.SelectedItem, selectedTrack) && Math.Abs(detailedScroll.VerticalOffset - detailedOffset) < 1, "Detailed list lost selection or scroll on back");
            await vm.BrowseAsync("收藏"); await vm.GoBackAsync(); await vm.BrowseAsync("本机历史"); await vm.GoBackAsync(); await Task.Delay(140); main.UpdateLayout();
            Check(ReferenceEquals(detailedList.SelectedItem, selectedTrack) && Math.Abs(detailedScroll.VerticalOffset - detailedOffset) < 1, "Rapid navigation captured a partially restored viewport");
            var queueList = (ListBox)main.FindName("QueueList"); var queueSearch = (TextBox)typeof(MainWindow).GetField("_queueSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            await vm.SetMobileOutputAsync(true); vm.ReplaceQueue(local.Take(5)); await vm.PlayAsync(4); queueSearch.Text = "夜航 1";
            ((Button)main.FindName("QueueFollowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(queueSearch.Text == "" && queueList.SelectedIndex == 4, "Locate current track failed through queue search");
            vm.ReplaceQueue([local[0], local[0], local[1]]); await vm.PlayAsync(1); queueList.SelectedIndex = 1;
            ((MenuItem)queueList.ContextMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(80);
            Check(vm.Queue.Count == 2 && vm.Current == local[1], "Duplicate queue row removed the wrong occurrence");
            Passed("详细列表返回恢复选择和滚动，队列搜索定位与重复项操作");
            foreach (var theme in ThemeCatalog.All)
            {
                Theme.Apply(theme.Id); vm.ApplyLyricAppearance();
                foreach (var size in new[] { (900d, 610d), (1200d, 800d) })
                {
                    main.Width = size.Item1; main.Height = size.Item2; await Task.Delay(60); main.UpdateLayout();
                    if (size.Item1 == 900)
                    {
                        var horizontal = UiVerification.FindAll<System.Windows.Controls.Primitives.ScrollBar>(detailedList).First(s => s.IsVisible && s.Orientation == Orientation.Horizontal);
                        Check(horizontal.ActualWidth > 100 && horizontal.ActualHeight <= 10, $"Horizontal scrollbar has vertical dimensions: {horizontal.ActualWidth}x{horizontal.ActualHeight}, width={horizontal.Width}, height={horizontal.Height}");
                        detailedScroll.ScrollToRightEnd(); main.UpdateLayout(); await Task.Delay(30); Check(detailedScroll.HorizontalOffset > 0, "Detailed columns cannot scroll horizontally"); detailedScroll.ScrollToLeftEnd(); main.UpdateLayout();
                    }
                    Check(UiVerification.FindAll<Button>(main).Where(b => b.IsVisible).All(b => b.ActualHeight >= 20), "Button target too small");
                    foreach (var input in UiVerification.FindAll<TextBox>(main).Where(b => b.IsVisible && b.ActualWidth > 0)) Check(input.ActualHeight >= 22, "Main input clipped");
                    layouts.Add(new { Window = "main", Theme = theme.Id, Width = size.Item1, Height = size.Item2, Dpi = VisualTreeHelper.GetDpi(main).PixelsPerDip });
                    if (theme.Id is "moon_white" or "ink_blue") UiVerification.Capture(main, Path.Combine(Store.Root, $"main-{theme.Id}-{size.Item1}.png"));
                }
            }
            foreach (bool lyrics in new[] { false, true }) foreach (string theme in new[] { "moon_white", "ink_blue" })
            {
                Theme.Apply(theme); var options = new ProductOptionsWindow(vm, lyrics) { Owner = main }; options.Show(); await Task.Delay(100); options.UpdateLayout(); UiVerification.Capture(options, Path.Combine(Store.Root, $"options-{lyrics}-{theme}.png")); Check(UiVerification.FindAll<ScrollViewer>(options).Any(), "Options have no scrolling"); options.Close();
            }
            var details = (ListView)typeof(MainWindow).GetField("_details", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            Check(!UiVerification.FindAll<TextBlock>(details).Any(t => t.Text == "ChiliMusic.Track"), "Detailed rows rendered object names");
            details.ScrollIntoView(details.Items[0]); await Task.Delay(80); main.UpdateLayout(); var firstRow = details.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement; Store.Write("detailed-row-diagnostics.json", new { Visible = details.IsVisible, Enabled = RowNumbering.GetEnabled(details), Index = firstRow == null ? -99 : RowNumbering.GetIndex(firstRow), Texts = firstRow == null ? null : UiVerification.FindAll<TextBlock>(firstRow).Select(t => new { t.Name, t.Text }).ToArray() }); Check(firstRow != null && UiVerification.FindAll<TextBlock>(firstRow).FirstOrDefault(t => t.Name == "RowNumber")?.Text == "1", "Detailed row numbers not one-based");
            string coverPath = Path.Combine(music, "cover.png");
            var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) { context.DrawRectangle(new LinearGradientBrush(Color.FromRgb(24, 51, 81), Color.FromRgb(62, 102, 143), 90), null, new Rect(0, 0, 320, 320)); context.DrawEllipse(new SolidColorBrush(Color.FromRgb(218, 227, 236)), null, new Point(235, 75), 21, 21); context.DrawText(new FormattedText("夜航", System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 42, Brushes.White, 1), new Point(38, 165)); }
            var art = new System.Windows.Media.Imaging.RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32); art.Render(drawing); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(art)); using (var output = File.Create(coverPath)) png.Save(output);
            foreach (var track in local) track.EmbeddedCoverPath = coverPath; list.CoverPath = coverPath; list.Tracks = local.Take(12).ToList(); vm.Playlists.Save(); vm.NotifyPlaylists(); await vm.SetMobileOutputAsync(true); await vm.PlayFromListAsync(local[1], local.Take(12)); vm.UpdateMobilePosition(96);
            foreach (var entry in new[] { ("下载中", 5L), ("已暂停", 3L), ("失败", 2L), ("完成", 10L) }) vm.Offline.Tasks.Add(new DownloadTask { Track = new Track { Id = "layout-" + entry.Item1, Title = "夜航 · " + entry.Item1, Artist = "歌手甲" }, State = entry.Item1, Bytes = entry.Item2 * 1048576, Total = 10 * 1048576, Error = entry.Item1 == "失败" ? "连接中断，可重试" : "" });
            foreach (string theme in new[] { "moon_white", "ink_blue" })
            {
                Theme.Apply(theme); await vm.BrowseAsync("夜航", item: new LibraryItem(PlayerViewModel.LocalAlbumId(local[0]), "夜航", "歌手甲", "local-album") { LocalCoverPath = coverPath }); main.Width = 1120; main.Height = 700; await Task.Delay(150); main.UpdateLayout(); UiVerification.Capture(main, Path.Combine(Store.Root, "reference-main-" + theme + ".png"));
                main.Width = 900; main.Height = 610; await Task.Delay(80); main.UpdateLayout(); var moreButton = (Button)main.FindName("BrowseMoreButton"); var heroText = (Grid)main.FindName("HeroTextGrid"); Check(moreButton.TranslatePoint(new System.Windows.Point(moreButton.ActualWidth, 0), heroText).X <= heroText.ActualWidth + 1, "Small album actions overflow"); UiVerification.Capture(main, Path.Combine(Store.Root, "reference-main-small-" + theme + ".png")); main.Width = 1120; main.Height = 700;
                foreach (string tab in new[] { "歌单", "离线" }) { var tools = new MusicToolsWindow(vm, tab) { Owner = main }; tools.Show(); await Task.Delay(150); tools.UpdateLayout(); UiVerification.Capture(tools, Path.Combine(Store.Root, "reference-tools-" + tab + "-" + theme + ".png")); tools.Close(); }
                var tags = new TagEditorWindow(vm, [tagged]) { Owner = main }; tags.Show(); await Task.Delay(80); UiVerification.Capture(tags, Path.Combine(Store.Root, "reference-tags-" + theme + ".png")); tags.Close();
                var settingsWindow = new SettingsWindow(vm) { Owner = main }; settingsWindow.Show(); settingsWindow.SelectPage(2); await Task.Delay(100); UiVerification.Capture(settingsWindow, Path.Combine(Store.Root, "reference-settings-" + theme + ".png")); settingsWindow.Close();
            }
            Passed("所有主题与两种主窗口尺寸渲染、新设置窗口浅深色检查");
            await Task.Delay(100); main.UpdateLayout(); details.SelectedItem = vm.Results[37]; details.ScrollIntoView(details.SelectedItem); details.UpdateLayout(); await Task.Delay(100);
            string selectedId = ((Track)details.SelectedItem).Id; double beforeRefresh = UiVerification.FindAll<ScrollViewer>(details).First().VerticalOffset;
            var updated = JsonSerializer.Deserialize<List<Track>>(JsonSerializer.Serialize(local))!; updated[0].Title = "更新后的曲目";
            var libraryUpdate = typeof(PlayerViewModel).GetMethod("OnLocalLibraryUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!; libraryUpdate.Invoke(vm, [updated]); await Task.Delay(180); main.UpdateLayout();
            Check(vm.BrowseItem?.Kind == "local-album" && ((Track?)details.SelectedItem)?.Id == selectedId && Math.Abs(UiVerification.FindAll<ScrollViewer>(details).First().VerticalOffset - beforeRefresh) < 1 && vm.Results.Any(t => t.Title == "更新后的曲目"), "Local metadata update lost detail viewport");
            foreach (var track in updated.Take(70)) track.Album = "更新后的专辑"; libraryUpdate.Invoke(vm, [updated]); await Task.Delay(180);
            Check(vm.Section == "更新后的专辑" && vm.Results.Count == 70, "Album rename lost current detail"); Passed("本地标签更新与专辑改名保留详情、选中项和滚动位置");
            await CoverVerification.RunAsync(app, main); Passed("服务器详情封面主动加载、回退、缓存、切换防串图及返回显示");
            Store.Write("product-verification.json", new { Result = "PASS", Passed = passed, Layouts = layouts });
        }
        catch (Exception error) { Store.Write("product-verification.json", new { Result = "FAIL", error.Message, error.StackTrace, Passed = passed, Layouts = layouts }); }
        finally { vm.Stop(); app.Exit(); }
    }
}
