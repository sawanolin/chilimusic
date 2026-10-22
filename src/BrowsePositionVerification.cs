using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal static class BrowsePositionVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task RunStandaloneAsync(App app)
    {
        try { await RunAsync(app); }
        catch (Exception error) { Store.Write("browse-verification.json", new { Result = "FAIL", error.Message, error.StackTrace }); }
        finally { app.Exit(); }
    }
    internal static async Task RunAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var passed = new List<string>();
        string server = vm.Settings.Server, username = vm.Settings.Username, source = vm.Settings.CatalogSource;
        using var fixture = new CatalogFixture();
        try
        {
            vm.Settings.Server = fixture.Url; vm.Settings.Username = "fixture"; vm.Settings.CatalogSource = "navidrome";
            vm.Api.Configure(vm.Settings, "fixture"); app.ShowMain();
            var main = Application.Current.Windows.OfType<MainWindow>().First(); main.Width = 900; main.Height = 610;
            var albums = (ListBox)main.FindName("LibraryList"); var songs = (ListBox)main.FindName("TracksList");
            var restoreTrace = new List<object>();
            vm.BrowseLeaving += () => restoreTrace.Add(new { Event = "leaving", vm.Section, Selected = (albums.SelectedItem as LibraryItem)?.Id, Offset = UiVerification.FindAll<ScrollViewer>(albums).FirstOrDefault()?.VerticalOffset });
            vm.BrowseArrived += state => restoreTrace.Add(new { Event = "arrived", vm.Section, Selected = state.SelectedAlbum?.Id, Offset = (double?)state.Albums });
            ScrollViewer Scroll(ListBox list) => UiVerification.FindAll<ScrollViewer>(list).First();
            async Task SetPosition(ListBox list, double position)
            {
                var selected = list.SelectedItems.Cast<object>().ToArray();
                var restoring = typeof(MainWindow).GetField("_restoringBrowse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                for (int i = 0; i < 100 && (bool)restoring.GetValue(main)!; i++)
                    await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check(!(bool)restoring.GetValue(main)!, "Browse restoration did not settle before scrolling");
                await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (list.SelectionMode == SelectionMode.Single) list.SelectedItem = selected.FirstOrDefault(item => list.Items.Contains(item));
                else { list.SelectedItems.Clear(); foreach (var item in selected) if (list.Items.Contains(item)) list.SelectedItems.Add(item); }
                Scroll(list).ScrollToVerticalOffset(position); main.UpdateLayout(); await Task.Delay(100); main.UpdateLayout();
                Check(Scroll(list).VerticalOffset > 0, $"Fixture list did not scroll: {vm.Section}/{list.Name}, visible={list.IsVisible}, height={list.ActualHeight}, count={list.Items.Count}, extent={Scroll(list).ExtentHeight}, viewport={Scroll(list).ViewportHeight}");
            }
            async Task Returned(string section, double offset, LibraryItem selection, string[] ids)
            {
                await Task.Delay(140); main.UpdateLayout();
                Check(vm.Section == section && vm.Library.Select(i => i.Id).SequenceEqual(ids), "Restored page lost loaded items: " + section);
                Check(Math.Abs(Scroll(albums).VerticalOffset - offset) < 1, $"Scroll not restored: {section}, expected={offset}, actual={Scroll(albums).VerticalOffset}");
                if (!ReferenceEquals(albums.SelectedItem, selection)) Store.Write("browse-restore-trace.json", restoreTrace);
                Check(ReferenceEquals(albums.SelectedItem, selection), $"Album selection not restored: {section}, expected={selection.Id}, actual={(albums.SelectedItem as LibraryItem)?.Id}, sameInstance={ReferenceEquals(vm.Library.FirstOrDefault(a => a.Id == selection.Id), selection)}");
            }
            foreach (string palette in new[] { "moon_white", "ink_blue" })
            {
                Theme.Apply(palette); await vm.BrowseAsync("最近添加", remember: false); await vm.LoadMoreAsync(); await Task.Delay(120);
                Check(vm.Library.Count == 120 && vm.HasMore, "Paged album fixture not loaded");
                var selected = vm.Library[78]; albums.SelectedItem = selected; await SetPosition(albums, 4800);
                double offset = Scroll(albums).VerticalOffset; string[] ids = vm.Library.Select(i => i.Id).ToArray();
                int requests = fixture.AlbumRequests;
                await vm.BrowseAsync(selected.Title, item: selected); await Task.Delay(120);
                Check(Scroll(songs).VerticalOffset == 0, "New detail page inherited old scroll");
                songs.SelectedItem = vm.Results[37]; await SetPosition(songs, 35); double songOffset = Scroll(songs).VerticalOffset;
                Track song = (Track)songs.SelectedItem; vm.FormatFilter = "WAV";
                await vm.BrowseAsync("搜索", "歌曲"); await vm.ClearSearchAsync(); await Task.Delay(140);
                Check(vm.FormatFilter == "WAV" && ReferenceEquals(songs.SelectedItem, song) && Math.Abs(Scroll(songs).VerticalOffset - songOffset) < .1,
                    $"Clearing search lost song position, filter or selection: format={vm.FormatFilter}, selected={songs.SelectedItem == song}, expected={songOffset}, actual={Scroll(songs).VerticalOffset}, saved={vm.BrowseViewport.Songs}, section={vm.Section}");
                await vm.GoBackAsync(); await Returned("最近添加", offset, selected, ids);
                Check(fixture.AlbumRequests == requests, "Back re-fetched album pages");
                UiVerification.Capture(main, Path.Combine(Store.Root, "browse-restored-" + palette + ".png"));
                await vm.LoadMoreAsync(); Check(vm.Library.Count == 180 && vm.Library[120].Id == "album-120", "Restored paging offset repeated or skipped albums");
                passed.Add(palette + ": paged recent albums, song scroll, search/filter/selection and continued paging");
            }
            await vm.BrowseAsync("专辑"); await vm.LoadMoreAsync(); await Task.Delay(120);
            var album = vm.Library[90]; albums.SelectedItem = album; await SetPosition(albums, 4800);
            double albumOffset = Scroll(albums).VerticalOffset; string[] albumIds = vm.Library.Select(i => i.Id).ToArray();
            await vm.BrowseAsync(album.Title, item: album);
            var navigation = (StackPanel)main.FindName("NavigationStack");
            navigation.Children.OfType<Button>().Single(b => (string)b.Content == "专辑").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Returned("专辑", albumOffset, album, albumIds); passed.Add("Sidebar returns to the existing album page");
            await vm.NavigateSectionAsync("专辑"); await Returned("专辑", albumOffset, album, albumIds);
            foreach (string section in new[] { "歌手", "歌单" })
            {
                await vm.BrowseAsync(section); await Task.Delay(120);
                var item = vm.Library[90]; albums.SelectedItem = item; await SetPosition(albums, 4800);
                double offset = Scroll(albums).VerticalOffset; string[] ids = vm.Library.Select(i => i.Id).ToArray();
                await vm.BrowseAsync(item.Title, item: item); await Task.Delay(120);
                if (section == "歌手")
                {
                    var nested = vm.Library[80]; albums.SelectedItem = nested; await SetPosition(albums, 4400);
                    double nestedOffset = Scroll(albums).VerticalOffset; string[] nestedIds = vm.Library.Select(i => i.Id).ToArray();
                    await vm.BrowseAsync(nested.Title, item: nested); await vm.GoBackAsync();
                    await Returned(item.Title, nestedOffset, nested, nestedIds);
                }
                await vm.GoBackAsync(); await Returned(section, offset, item, ids); passed.Add(section + ": nested back preserves each page independently");
            }
            Task pending = vm.BrowseAsync("延迟专辑", item: new LibraryItem("slow", "延迟专辑", "", "album"));
            await Task.Delay(30); await vm.GoBackAsync();
            try { await pending; } catch (OperationCanceledException) { }
            await Task.Delay(140); Check(vm.Section == "歌单" && vm.Library.Count == 120 && vm.Results.Count == 0, "Cancelled detail page overwrote the restored page");
            passed.Add("Rapid back cancels stale page requests and scroll callbacks");
            await vm.BrowseAsync("最近添加", remember: false); await vm.LoadMoreAsync(); await Task.Delay(120);
            var paged = vm.Library[78]; albums.SelectedItem = paged; await SetPosition(albums, 4800);
            double pagedOffset = Scroll(albums).VerticalOffset; string[] pagedIds = vm.Library.Select(i => i.Id).ToArray();
            Task loading = vm.LoadMoreAsync(); await Task.Delay(30);
            await vm.BrowseAsync(paged.Title, item: paged); await vm.GoBackAsync();
            try { await loading; } catch (OperationCanceledException) { }
            await Returned("最近添加", pagedOffset, paged, pagedIds);
            await vm.LoadMoreAsync(); Check(vm.Library.Count == 180 && vm.Library[120].Id == "album-120", "Cancelled paging lost continuation offset");
            passed.Add("Entering a detail during pagination retains loaded pages and resumes the cancelled next page");
            Store.Write("browse-verification.json", new { Result = "PASS", Passed = passed });
        }
        finally { vm.Settings.Server = server; vm.Settings.Username = username; vm.Settings.CatalogSource = source; vm.Api.Configure(vm.Settings, vm.Password); }
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _albumRequests;
        public int AlbumRequests => _albumRequests;
        public string Url { get; }
        public CatalogFixture()
        {
            _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ServeAsync(client); } }
            catch (OperationCanceledException) { } catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string? line = await reader.ReadLineAsync(_stop.Token); if (line == null) return;
                var uri = new Uri(Url + line.Split(' ')[1]);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string endpoint = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                var root = new Dictionary<string, object> { ["status"] = "ok" };
                object[] Albums(int first, int count) => Enumerable.Range(first, count).Select(i => (object)new { id = "album-" + i, name = "专辑 " + i, artist = "歌手" }).ToArray();
                object[] Songs() => Enumerable.Range(1, 73).Select(i => (object)new { id = "song-" + i, title = "歌曲 " + i, artist = "歌手", album = "测试专辑", suffix = "wav", duration = 120, track = i }).ToArray();
                switch (endpoint)
                {
                    case "getAlbumList2":
                        Interlocked.Increment(ref _albumRequests); int offset = int.Parse(query["offset"] ?? "0");
                        if (offset == 120) await Task.Delay(200, _stop.Token);
                        root["albumList2"] = new { album = Albums(offset, Math.Max(0, Math.Min(60, 180 - offset))) }; break;
                    case "getAlbum":
                        if (query["id"] == "slow") await Task.Delay(350, _stop.Token);
                        root["album"] = new { song = Songs() }; break;
                    case "getArtist": root["artist"] = new { album = Albums(0, 120) }; break;
                    case "getArtists": root["artists"] = new { index = new[] { new { artist = Enumerable.Range(0, 120).Select(i => new { id = "artist-" + i, name = "歌手 " + i }).ToArray() } } }; break;
                    case "getPlaylists": root["playlists"] = new { playlist = Enumerable.Range(0, 120).Select(i => new { id = "playlist-" + i, name = "歌单 " + i }).ToArray() }; break;
                    case "getPlaylist": root["playlist"] = new { entry = Songs() }; break;
                    case "search3": root["searchResult3"] = new { song = Songs().Take(4).ToArray(), album = Albums(0, 4) }; break;
                    default: throw new InvalidOperationException("Unexpected fixture endpoint: " + endpoint);
                }
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["subsonic-response"] = root });
                byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token); await stream.WriteAsync(body, _stop.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or SocketException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
