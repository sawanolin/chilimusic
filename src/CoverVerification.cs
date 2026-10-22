using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChiliMusic;

internal static class CoverVerification
{
    internal static async Task RunAsync(App app, MainWindow main)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm;
        string server = vm.Settings.Server, username = vm.Settings.Username, source = vm.Settings.CatalogSource;
        var previousCover = vm.Cover; using var fixture = new CoverFixture();
        var hero = (Image)main.FindName("HeroCover"); var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Expect(byte red, string message)
        {
            for (int i = 0; i < 100 && Red(hero.Source) != red; i++) await Task.Delay(20);
            Check(Red(hero.Source) == red, message); main.UpdateLayout();
        }
        try
        {
            vm.Settings.Server = fixture.Url; vm.Settings.Username = "fixture"; vm.Settings.CatalogSource = "navidrome"; vm.Api.Configure(vm.Settings, "fixture");
            await vm.BrowseAsync("服务器专辑", item: new("remote-album", "服务器专辑", "歌手", "album", "album-art"));
            await Expect(40, "Server album detail did not fetch its cover without a thumbnail");
            Check(!ReferenceEquals(hero.Source, vm.Cover) && ReferenceEquals(previousCover, vm.Cover), "Album detail reused or changed the playing cover"); checks.Add("服务器专辑无需预加载缩略图即可独立请求封面，不影响当前播放封面");
            Theme.Apply("moon_white"); UiVerification.Capture(main, Path.Combine(Store.Root, "cover-server-light.png"));
            int requests = fixture.CoverRequests.GetValueOrDefault("album-art"); main.Width = 900; main.Height = 610; Theme.Apply("ink_blue"); await Task.Delay(100);
            await Expect(40, "Resize or theme change cleared album cover"); Check(fixture.CoverRequests.GetValueOrDefault("album-art") == requests, "Layout change fetched cover again");
            UiVerification.Capture(main, Path.Combine(Store.Root, "cover-server-dark-small.png")); checks.Add("浅深主题与小窗口保留封面，布局变化不重复下载");
            await vm.BrowseAsync("歌曲封面回退", item: new("fallback", "歌曲封面回退", "歌手", "album")); await Expect(210, "Missing album cover ID did not fall back to its tracks");
            await vm.BrowseAsync("无效专辑封面", item: new("bad", "无效专辑封面", "歌手", "album", "bad-art")); await Expect(210, "Failed album cover did not fall back to its tracks"); checks.Add("缺少或失效的专辑封面标识可回退到当前专辑歌曲封面");
            await vm.BrowseAsync("服务器歌单", item: new("playlist", "服务器歌单", "歌手", "playlist", "album-art")); await Expect(40, "Server playlist detail cover missing"); checks.Add("服务器歌单详情同样加载远程封面");
            await vm.BrowseAsync("慢专辑", item: new("slow", "慢专辑", "歌手", "album", "slow-art")); await fixture.SlowRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await vm.BrowseAsync("新专辑", item: new("fast", "新专辑", "歌手", "album", "fast-art")); await Expect(60, "New album cover missing"); await Task.Delay(500); Check(Red(hero.Source) == 60, "Stale cover overwrote current album"); checks.Add("慢请求返回后不会覆盖已经切换的新专辑封面");
            await vm.BrowseAsync("离开详情"); await vm.GoBackAsync(); await Expect(60, "Back navigation did not restore detail cover");
            main.Hide(); main.Show(); await Expect(60, "Reopening window did not restore detail cover"); checks.Add("返回详情与隐藏再显示窗口重新显示封面");
            string localArt = Path.Combine(Store.Root, "local-cover-fixture.png"); await File.WriteAllBytesAsync(localArt, CoverFixture.Png(170));
            var list = vm.Playlists.Create("本地封面", []); list.CoverPath = localArt; vm.Playlists.Save();
            await vm.BrowseAsync("本地封面", item: new(list.Id, "本地封面", "本地歌单", "local-playlist") { LocalCoverPath = localArt }); await Expect(170, "Local detail cover regressed");
            var empty = vm.Playlists.Create("没有封面", []); await vm.BrowseAsync("没有封面", item: new(empty.Id, "没有封面", "本地歌单", "local-playlist")); await Task.Delay(80); Check(hero.Source == null, "No-art detail reused previous album art"); checks.Add("本地自选封面正常，无封面项目清除旧图");
            Store.Write("cover-verification.json", new { Result = "PASS", Checks = checks, Requests = fixture.CoverRequests });
        }
        finally { vm.Settings.Server = server; vm.Settings.Username = username; vm.Settings.CatalogSource = source; vm.Api.Configure(vm.Settings, vm.Password); Theme.Apply(vm.Settings.Theme); }
    }
    private static byte? Red(ImageSource? source)
    {
        if (source is not BitmapSource bitmap) return null; var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); byte[] pixel = new byte[4]; converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0); return pixel[2];
    }
    private sealed class CoverFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public ConcurrentDictionary<string, int> CoverRequests { get; } = new();
        public TaskCompletionSource SlowRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        internal CoverFixture() { _listener.Start(); _ = AcceptAsync(); }
        internal static byte[] Png(byte red)
        {
            var pixels = Enumerable.Range(0, 64 * 64).SelectMany(_ => new byte[] { 100, 130, red, 255 }).ToArray();
            var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var output = new MemoryStream(); png.Save(output); return output.ToArray();
        }
        private readonly Dictionary<byte, byte[]> _images = new() { [40] = Png(40), [60] = Png(60), [210] = Png(210) };
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ServeAsync(client); } }
            catch (OperationCanceledException) { } catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client) try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); string? request = await reader.ReadLineAsync(_stop.Token); if (request == null) return;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                var uri = new Uri(Url + request.Split(' ')[1]); string endpoint = Path.GetFileNameWithoutExtension(uri.AbsolutePath); string id = System.Web.HttpUtility.ParseQueryString(uri.Query)["id"] ?? "";
                byte[] body; string type = "application/json"; int status = 200;
                if (endpoint == "getCoverArt")
                {
                    CoverRequests.AddOrUpdate(id, 1, (_, value) => value + 1);
                    if (id == "slow-art") { SlowRequested.TrySetResult(); await Task.Delay(350, _stop.Token); }
                    status = id == "bad-art" ? 404 : 200; type = "image/png"; body = _images[id == "fast-art" ? (byte)60 : id == "track-art" ? (byte)210 : (byte)40];
                }
                else
                {
                    object[] songs = [new { id = "fixture-track-" + id, title = "服务器歌曲", artist = "歌手", album = "服务器专辑", duration = 120, coverArt = "track-art" }];
                    var root = new Dictionary<string, object> { ["status"] = "ok" };
                    if (endpoint == "getAlbum") root["album"] = new { song = songs };
                    else if (endpoint == "getPlaylist") root["playlist"] = new { entry = songs };
                    else if (endpoint == "getPlaylists") root["playlists"] = new { playlist = Array.Empty<object>() };
                    else root["albumList2"] = new { album = Array.Empty<object>() };
                    body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["subsonic-response"] = root });
                }
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), _stop.Token); await stream.WriteAsync(body, _stop.Token);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
