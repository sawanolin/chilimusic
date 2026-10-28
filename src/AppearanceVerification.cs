using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pen = System.Windows.Media.Pen;
using FlowDirection = System.Windows.FlowDirection;

namespace ChiliMusic;

// Offline visual acceptance: only generated artwork/audio in an isolated QA profile.
internal static class AppearanceVerification
{
    internal static async Task RunAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile();
        var vm = app.Vm; var passed = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); passed.Add(message); }
        async Task Capture(FrameworkElement element, string name)
        { await Task.Delay(160); element.UpdateLayout(); UiVerification.Capture(element, Path.Combine(Store.Root, name + ".png")); }
        try
        {
            var gray = BitmapSource.Create(96, 96, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(new byte[] { 100, 100, 100, 255 }, 96 * 96).SelectMany(x => x).ToArray(), 384);
            Check(CoverPalette.Sample(gray).Primary == null, "灰阶封面不生成错误强调色");
            string fixtures = Path.Combine(Store.Root, "fixtures"); Directory.CreateDirectory(fixtures);
            string[] albums = ["夜航 / NIGHT FLIGHT", "薄雾中的来信", "After the rain", "月亮与海", "静谧回声", "慢慢地走"];
            string[] titles = ["夜航", "穿过城市的风", "无人知晓的清晨", "沿途的光", "等一场雨停", "回声", "夜色温柔", "与海相拥", "远方来信", "慢慢地走", "另一种蓝", "晚安，明天见"];
            var files = new List<string>();
            for (int album = 0; album < albums.Length; album++)
            {
                string directory = Path.Combine(fixtures, album.ToString()); Directory.CreateDirectory(directory);
                var art = Artwork(album, albums[album]); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(art)); using var memory = new MemoryStream(); encoder.Save(memory); string cover = Path.Combine(directory, "cover.png"); File.WriteAllBytes(cover, memory.ToArray());
                for (int i = 0; i < (album == 0 ? 12 : 1); i++)
                {
                    string file = Path.Combine(directory, i + ".wav"); FeatureVerification.WriteWave(file, 90, 0);
                    TagEditorWindow.Write(file, new Dictionary<string, string> { ["标题"] = titles[i], ["歌手"] = album == 0 ? "林间 / LIN JIAN" : "Chili Sessions", ["专辑"] = albums[album], ["曲目号"] = (i + 1).ToString(), ["年份"] = "2026" }, cover);
                    files.Add(file);
                }
            }
            await vm.LocalLibrary.InitializeAsync(); await vm.LocalLibrary.ImportAsync(files); await vm.BrowseAsync("本地音乐");
            app.ShowMain(); var main = Application.Current.Windows.OfType<MainWindow>().First(); await Task.Delay(250);
            var tracks = vm.LocalLibrary.Snapshot.Where(t => t.Album == albums[0]).OrderBy(t => t.TrackNumber).ToArray();
            var original = Enumerable.Range(0, 12).Select(i => new LyricLine(i * 6, new[] { "让风带走今天的疲惫", "穿过城市闪烁的灯火", "我们在夜色里缓缓前行", "在每一段旋律中找到自己", "还有远方的光，和未完的故事", "晚安，明天见" }[i % 6])).ToList();
            var translated = Enumerable.Range(0, 12).Select(i => new LyricLine(i * 6, new[] { "Let the wind carry the day away", "Through the lights of the city", "We wander slowly through the night", "Find ourselves in every melody", "A distant light, a story still unfolding", "Good night, see you tomorrow" }[i % 6])).ToList();
            Store.Write(vm.Lyrics.CachePath(tracks[2]) + ".match.json", new List<LyricsDocument> { new("歌词", original), new("翻译", translated) });
            await vm.SetMobileOutputAsync(true); await vm.PlayFromListAsync(tracks[2], tracks); vm.UpdateMobilePosition(24);
            for (int i = 0; i < 100 && vm.LyricLines.Count == 0; i++) await Task.Delay(30);
            Check(vm.LyricLines.Count == 12 && vm.HasLyricTranslation, "真实歌词加载路径恢复测试歌词与翻译");
            if (Environment.GetCommandLineArgs().Contains("--qa-interactions-only"))
            {
                await InteractionVerification.RunAsync(app, main);
                Store.Write("appearance-report.json", new { Result = "PASS", Passed = passed, InteractionsOnly = true }); return;
            }
            foreach (string theme in new[] { "sonora_dark", "sonora_light" })
            {
                Theme.Apply(theme); vm.Settings.LocalView = "专辑"; vm.Changed(nameof(vm.LocalView)); await vm.BrowseAsync("本地音乐"); main.Width = 1200; main.Height = 800; await Capture(main, "library-" + theme);
                var album = vm.Library.First(item => item.Title == albums[0]);
                vm.Settings.LocalView = "歌手"; vm.Changed(nameof(vm.LocalView)); await vm.BrowseAsync("本地音乐"); await Capture(main, "artists-" + theme);
                var musician = vm.Library.First(); await vm.BrowseAsync(musician.Title, item: musician); await Capture(main, "artist-detail-" + theme);
                await vm.BrowseAsync("搜索", "夜"); await Capture(main, "search-" + theme);
                await vm.BrowseAsync("搜索", "__no_result__"); await Capture(main, "empty-" + theme);
                vm.Settings.LocalView = "专辑"; vm.Changed(nameof(vm.LocalView)); await vm.BrowseAsync(album.Title, item: album);
                await Capture(main, "detail-" + theme);
                var details = (ListView)typeof(MainWindow).GetField("_details", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(main)!; details.SelectedIndex = 0;
                var songs = (ListBox)main.FindName("TracksList"); var menu = songs.ContextMenu!; menu.PlacementTarget = songs; menu.IsOpen = true;
                await Capture(menu, "menu-" + theme); menu.IsOpen = false;
                ((Button)main.FindName("PlayListButton")).Focus(); await Capture(main, "focus-" + theme);
                ((Button)main.FindName("FilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var filter = (System.Windows.Controls.Primitives.Popup)main.FindName("FilterPopup"); await Capture((FrameworkElement)filter.Child, "filters-" + theme); filter.IsOpen = false;
                main.Width = 900; main.Height = 610; await Capture(main, "small-" + theme);
                var more = (Button)main.FindName("BrowseMoreButton"); var hero = (Grid)main.FindName("HeroTextGrid");
                Check(more.TranslatePoint(new Point(more.ActualWidth, 0), hero).X <= hero.ActualWidth + 1, theme + " 最小窗口操作组可见");
                main.Width = 1200; main.Height = 800;
                var settings = new SettingsWindow(vm) { Owner = main }; settings.Show();
                var themes = (ComboBox)settings.FindName("ThemeBox"); themes.IsDropDownOpen = true; await Task.Delay(80);
                if (themes.Template.FindName("PART_Popup", themes) is System.Windows.Controls.Primitives.Popup dropdown) await Capture((FrameworkElement)dropdown.Child, "themes-" + theme); themes.IsDropDownOpen = false;
                for (int page = 0; page < 7; page++) { settings.SelectPage(page); await Capture(settings, $"settings-{page}-{theme}"); } settings.Close();
                foreach (string tab in new[] { "歌单", "离线", "定时", "歌词" }) { var tools = new MusicToolsWindow(vm, tab) { Owner = main }; tools.Show(); await Capture(tools, $"tools-{tab}-{theme}"); tools.Close(); }
                var account = new NeteaseAccountWindow(vm, false) { Owner = main }; account.Show(); await Capture(account, "account-" + theme); account.Close();
                app.ShowMini(); var mini = Application.Current.Windows.OfType<MiniPlayerWindow>().First(); await Capture(mini, "mini-" + theme); mini.Hide();
                app.ShowMain(); app.ShowImmersive(); var immersive = Application.Current.Windows.OfType<ImmersiveWindow>().First();
                Check(vm.LyricLines.Count == 12, theme + " 歌词截图包含完整歌词"); immersive.Lyrics.Center(); await Task.Delay(450); await Capture(immersive, "lyrics-" + theme); immersive.Close();
            }
            await InteractionVerification.RunAsync(app, main);
            Theme.Apply("sonora_dark");
            Check(Theme.Contrast(((SolidColorBrush)Application.Current.Resources["AccentFillBrush"]).Color, ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color) >= 4.5, "主按钮文字对比度");
            Store.Write("appearance-report.json", new { Result = "PASS", Passed = passed, Screenshots = Directory.GetFiles(Store.Root, "*.png").Length });
        }
        catch (Exception e) { Store.Write("appearance-report.json", new { Result = "FAIL", e.Message, e.StackTrace, Passed = passed }); }
        finally { app.Exit(); }
    }
    private static BitmapSource Artwork(int index, string title)
    {
        var colors = new[] { Color.FromRgb(44, 111, 126), Color.FromRgb(150, 101, 100), Color.FromRgb(95, 106, 155), Color.FromRgb(124, 137, 96), Color.FromRgb(143, 110, 65), Color.FromRgb(105, 90, 136) };
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(8, 19, 24), colors[index], 50), null, new Rect(0, 0, 480, 480));
            for (int i = 0; i < 7; i++) dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(28, 230, 240, 235)), 1.5), new Point(380, 110), 45 + i * 35, 45 + i * 35);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(228, 222, 197)), null, new Point(358, 128), 32, 32);
            dc.DrawText(new FormattedText("CHILI SESSIONS  /  0" + (index + 1), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.White, 1), new Point(32, 36));
            dc.DrawText(new FormattedText(title.Split('/')[0], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 32, Brushes.White, 1), new Point(32, 340));
            dc.DrawText(new FormattedText("SOUNDTRACK FOR SLOWER DAYS", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, 1), new Point(34, 405));
        }
        var bitmap = new RenderTargetBitmap(480, 480, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
