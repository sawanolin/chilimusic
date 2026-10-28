using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ChiliMusic;

internal static class InteractionVerification
{
    internal static async Task RunAsync(App app, MainWindow main)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var passed = new List<string>();
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); passed.Add(text); }
        async Task Settle() { await Task.Delay(150); main.UpdateLayout(); }
        try
        {
            app.ShowMain();
            vm.Settings.LocalView = "歌曲"; vm.Changed(nameof(vm.LocalView)); await vm.BrowseAsync("本地音乐");
            var view = CollectionViewSource.GetDefaultView(vm.Results); view.SortDescriptions.Add(new(nameof(Track.Title), ListSortDirection.Descending));
            string[] ordered = view.Cast<Track>().Select(t => t.Id).ToArray(); var queueBefore = vm.Queue.ToArray();
            await vm.BrowseAsync("收藏"); Check(view.SortDescriptions.Count == 0, "新页面不继承其他页面的歌曲排序");
            await vm.GoBackAsync(); await Settle();
            Check(view.Cast<Track>().Select(t => t.Id).SequenceEqual(ordered) && vm.CanGoForward, "返回恢复排序并提供前进历史");
            await vm.GoForwardAsync(); Check(vm.Section == "收藏", "前进恢复目标页面");
            await vm.GoBackAsync(); await vm.BrowseAsync("本机历史");
            Check(!vm.CanGoForward && vm.Queue.SequenceEqual(queueBefore), "新导航清除前进分支且浏览不改变队列");
            await vm.BrowseAsync("本地音乐"); await vm.BrowseAsync("搜索", "夜"); await vm.GoBackAsync(); await vm.GoForwardAsync(); await vm.ClearSearchAsync();
            Check(vm.Section == "本地音乐" && vm.CanGoForward, "前进到搜索后清空搜索能恢复原页面");
            vm.Settings.LocalView = "专辑"; vm.Changed(nameof(vm.LocalView)); await vm.BrowseAsync("本地音乐"); await Settle();
            var library = (ListBox)main.FindName("LibraryList"); var album = vm.Library.First(); library.ScrollIntoView(album); await Settle();
            var row = (ListBoxItem)library.ItemContainerGenerator.ContainerFromItem(album);
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent }); await Settle();
            Check(vm.BrowseItem?.Id == album.Id, "单击专辑卡片进入详情");
            await vm.GoBackAsync(); await Settle();
            Check(vm.LocalView == "专辑" && Equals(((ComboBox)main.FindName("LocalViewBox")).SelectedItem, "专辑"), "返回后本地视图选择框与页面一致");
            library.ScrollIntoView(vm.Library.First()); await Settle(); row = (ListBoxItem)library.ItemContainerGenerator.ContainerFromIndex(0);
            var play = UiVerification.FindAll<Button>(row).Single(b => b.Name == "CardPlay");
            play.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Check(typeof(MainWindow).GetField("_pressedLibrary", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main) == null, "播放按钮不会触发卡片导航");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(); Check(vm.BrowseItem == null && vm.Current != null, "卡片播放保留当前浏览页面");
            // Use the album with enough tracks for queue mutation checks.
            var tracks = vm.LocalLibrary.Snapshot.GroupBy(t => t.AlbumKey).OrderByDescending(g => g.Count()).First().OrderBy(t => t.TrackNumber).ToArray();
            var detail = new LibraryItem(PlayerViewModel.LocalAlbumId(tracks[0]), tracks[0].Album, tracks[0].Artist, "local-album");
            await vm.BrowseAsync(detail.Title, item: detail); await vm.PlayCompleteBrowseAsync(tracks[2]); vm.UpdateMobilePosition(24);
            var identity = vm.Current; var originalQueue = vm.Queue.ToArray(); await vm.ToggleBrowsePlaybackAsync();
            Check(!vm.MobilePlaying && ReferenceEquals(vm.Current, identity) && vm.Position == 24 && vm.Queue.SequenceEqual(originalQueue), "列表暂停保留歌曲、位置和队列");
            await vm.ToggleBrowsePlaybackAsync(); Check(vm.MobilePlaying && vm.Position == 24, "列表继续播放不从第一首重来");
            vm.ReplaceQueue([tracks[0], tracks[1], tracks[0], tracks[2], tracks[3]]); await vm.PlayAsync(3); vm.UpdateMobilePosition(15);
            var search = (TextBox)typeof(MainWindow).GetField("_queueSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            search.Text = tracks[0].Title; var queue = (ListBox)main.FindName("QueueList"); await Settle();
            Check(queue.Items.Count == 2, "队列筛选保留重复歌曲的独立行"); queue.SelectAll(); main.RemoveSelectedQueueRows();
            Check(vm.Queue.Count == 3 && vm.Queue.All(t => t.Id != tracks[0].Id) && vm.Current?.Id == tracks[2].Id && vm.Position == 15, "筛选后的批量删除定位正确且保留当前播放");
            await vm.UndoQueueAsync(); Check(vm.Queue.Count == 5 && vm.Current?.Id == tracks[2].Id && vm.Position == 15, "一次撤销完整恢复批量删除"); search.Clear();
            Store.Write("interaction-report.json", new { Result = "PASS", Passed = passed });
        }
        catch (Exception error) { Store.Write("interaction-report.json", new { Result = "FAIL", error.Message, error.StackTrace, Passed = passed }); throw; }
    }
}
