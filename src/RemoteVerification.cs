using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChiliMusic;

internal static class RemoteVerification
{
    internal static async Task RunAsync(App app, TaskbarService taskbar)
    {
        FeatureVerification.RequireIsolatedProfile(); var vm = app.Vm; var report = new Dictionary<string, object>();
        void Record(string name, object value) { report[name] = value; Store.Write("remote-verification.json", report); }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        try
        {
            var wave = Path.Combine(Store.Root, "phone.wav");
            using (var file = File.Create(wave)) using (var writer = new BinaryWriter(file)) { const int size = 44100 * 2 * 40; writer.Write("RIFF"u8.ToArray()); writer.Write(size + 36); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8.ToArray()); writer.Write(size); for (int i = 0; i < 44100 * 40; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 600)); }
            vm.Settings.Mode = PlayMode.Sequential;
            await vm.LocalLibrary.InitializeAsync(); await vm.LocalLibrary.ImportAsync([wave]);
            var local = vm.LocalLibrary.Snapshot.First(t => t.LocalPath == wave); local.Title = "夜晚散步 · 一个很长的中文歌名与英文 Mixed Title"; local.Artist = "chilimusic"; local.Album = "本地音乐";
            vm.ReplaceQueue([local, new Track { Id = "qa2", Title = "第二首 <script>alert(1)</script>", Artist = "中文歌手与英文 Artist", Album = "手机播放验证", LocalPath = wave, Suffix = "wav", Duration = 40 }]); await vm.PlayAsync(0); await Task.Delay(1200); vm.PausePlayback();
            app.Remote.Start(47833); var origin = "http://127.0.0.1:" + app.Remote.Port;
            var cookie = new CookieContainer(); using var http = new HttpClient(new HttpClientHandler { UseProxy = false, CookieContainer = cookie }) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(20) };
            string token = "";
            async Task<HttpResponseMessage> Send(string path, object? body = null, bool auth = true, string? requestOrigin = null)
            {
                var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
                if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                if (auth && token.Length > 0) request.Headers.Add("X-Chili-Token", token); request.Headers.Add("Origin", requestOrigin ?? origin);
                var response = await http.SendAsync(request); request.Dispose(); return response;
            }
            using (var unauth = await Send("/api/state", auth: false)) Check(unauth.StatusCode == HttpStatusCode.Unauthorized, "unpaired control");
            using (var wrong = await Send("/api/pair", new { code = "000000" }, false)) Check(wrong.StatusCode == HttpStatusCode.Unauthorized, "bad pairing");
            using (var foreign = await Send("/api/pair", new { code = app.Remote.PairCode }, false, "http://evil.invalid")) Check(foreign.StatusCode == HttpStatusCode.Forbidden, "cross-origin pairing");
            using (var paired = await Send("/api/pair", new { code = app.Remote.PairCode }, false)) { Check(paired.IsSuccessStatusCode, "pairing"); using var json = JsonDocument.Parse(await paired.Content.ReadAsStringAsync()); token = json.RootElement.GetProperty("token").GetString()!; Check(paired.Headers.GetValues("Set-Cookie").Single().Contains("HttpOnly; SameSite=Strict"), "audio cookie protection"); }
            JsonElement snapshot;
            using (var response = await Send("/api/state")) { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); snapshot = json.RootElement.Clone(); }
            string text = snapshot.ToString(); foreach (string sensitive in new[] { vm.Settings.Server, vm.Settings.Username, vm.Password, wave }.Where(s => s.Length >= 5)) Check(!text.Contains(sensitive), "private information in remote state");
            string key = snapshot.GetProperty("current").GetProperty("key").GetString()!;
            using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/audio?key=" + key)) { request.Headers.Range = new(0, 63); using var response = await http.SendAsync(request); Check(response.StatusCode == HttpStatusCode.PartialContent && (await response.Content.ReadAsByteArrayAsync()).Length == 64, "audio byte ranges"); }
            async Task<JsonElement> Command(object value) { using var response = await Send("/api/command", value); Check(response.IsSuccessStatusCode, "command " + JsonSerializer.Serialize(value)); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone(); }
            snapshot = await Command(new { action = "output-phone" }); Check(vm.MobileOutput && vm.Player.IsIdle, "desktop must be silent for phone output");
            await Command(new { action = "play" }); Check(vm.MobilePlaying, "phone play"); await Command(new { action = "phone-position", key, value = 7.5 }); Check(Math.Abs(vm.Position - 7.5) < .1, "phone position");
            await Command(new { action = "pause" }); Check(!vm.MobilePlaying, "phone pause"); await Command(new { action = "seek", value = 12.0 }); Check(Math.Abs(vm.Position - 12) < .1, "phone seek");
            await Command(new { action = "output-computer", value = 12.0 }); await Task.Delay(900); Check(!vm.MobileOutput && vm.Player.IsPaused && vm.Player.Position > 10, "resume computer position while paused");
            await Command(new { action = "volume", value = 14.0 }); Check(vm.Volume == 14, "volume"); await Command(new { action = "mode" }); Check(vm.Settings.Mode == PlayMode.RepeatAll, "play mode");
            int revision = snapshot.GetProperty("revision").GetInt32(); await Command(new { action = "queue-move", revision, index = 0, to = 1 });
            using (var stale = await Send("/api/command", new { action = "queue-remove", revision, index = 0 })) Check(stale.StatusCode == HttpStatusCode.Conflict, "stale queue guard");
            using (var localResults = await Send("/api/library?source=local&query=%E5%A4%9C%E6%99%9A")) Check(localResults.IsSuccessStatusCode && !(await localResults.Content.ReadAsStringAsync()).Contains(wave), "local library privacy");
            await Command(new { action = "output-phone" }); await Command(new { action = "play" }); vm.SetSleep(0, true);
            snapshot = await Command(new { action = "phone-ended", key }); Check(!vm.MobilePlaying, "phone sleep after current");
            await Command(new { action = "play" }); int beforeNext = vm.Index;
            await Command(new { action = "phone-ended", key }); Check(vm.Index != beforeNext, "phone automatic next");
            int afterNext = vm.Index; await Command(new { action = "phone-ended", key }); Check(vm.Index == afterNext, "ignore stale phone ended");
            await Command(new { action = "output-computer" }); await Command(new { action = "pause" });
            foreach (string path in new[] { "/", "/app.js", "/style.css" }) using (var asset = await Send(path, auth: false)) Check(asset.IsSuccessStatusCode && asset.Headers.Contains("Content-Security-Policy"), "embedded web assets");
            foreach (string invalid in new[] { "GET /api/state HTTP/1.1\r\nHost: evil.invalid\r\n\r\n", "POST /api/pair HTTP/1.1\r\nHost: 127.0.0.1:" + app.Remote.Port + "\r\nContent-Length: 9000\r\n\r\n", "POST /api/pair HTTP/1.1\r\nHost: 127.0.0.1:" + app.Remote.Port + "\r\nContent-Length: 0\r\nContent-Length: 0\r\n\r\n", "POST /api/pair HTTP/1.1\r\nHost: 127.0.0.1:" + app.Remote.Port + "\r\nTransfer-Encoding: chunked\r\n\r\n" })
            {
                using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, app.Remote.Port); var stream = client.GetStream(); await stream.WriteAsync(Encoding.ASCII.GetBytes(invalid)); byte[] bytes = new byte[512]; int count = await stream.ReadAsync(bytes); string response = Encoding.ASCII.GetString(bytes, 0, count); Check(response.StartsWith("HTTP/1.1 400") || response.StartsWith("HTTP/1.1 403"), "invalid HTTP request rejected");
            }
            for (int attempt = 0; attempt < 6; attempt++) using (var bad = await Send("/api/pair", new { code = "000000" }, false)) { if (attempt == 5) Check(bad.StatusCode == (HttpStatusCode)429, "pairing attempt limit"); }
            app.Remote.ResetPairing(); using (var revoked = await Send("/api/state")) Check(revoked.StatusCode == HttpStatusCode.Unauthorized, "revoke devices");
            Record("remoteProtocol", "PASS");
            if (vm.Api.Configured)
            {
                var songs = await vm.Api.RandomAsync(); var serverTrack = songs.FirstOrDefault(t => t.Suffix == "flac") ?? songs.FirstOrDefault();
                if (serverTrack != null) { vm.ReplaceQueue([serverTrack, local]); await vm.PlayAsync(0); await Task.Delay(1800); vm.PausePlayback(); Record("serverPlayback", "PASS"); }
            }
            var palettes = new List<object>(); app.ShowMain(); var main = Application.Current.Windows.Cast<Window>().OfType<MainWindow>().First();
            void Capture(FrameworkElement element, string name) => UiVerification.Capture(element, Path.Combine(Store.Root, name + ".png"));
            void WindowCheck(Window window, string name)
            {
                Check(window.Content is Border, "window surface " + name); var surface = (Border)window.Content; Check(surface.CornerRadius.TopLeft == 10 && surface.CornerRadius.BottomRight == 10, "window radius " + name);
                Check(surface.Child is FrameworkElement { Clip: RectangleGeometry }, "window corner clip " + name); var content = (FrameworkElement)surface.Child; var clip = (RectangleGeometry)content.Clip; Check(clip.RadiusX == 9 && Math.Abs(clip.Rect.Width - content.ActualWidth) < 1, "window inner clipping " + name);
                foreach (var button in UiVerification.FindAll<Button>(window).Where(b => b.IsVisible && b.Content is string && b.ActualWidth > 0)) Check(button.ActualHeight >= 24, "button height " + name + " " + button.Content + " " + button.ActualHeight);
                Capture(window, name);
            }
            foreach (var theme in ThemeCatalog.All)
            {
                Theme.SetCoverAccent(null, false); Theme.Apply(theme.Id); await Task.Delay(70); vm.Settings.Theme = theme.Id;
                Color BrushColor(string name) => ((SolidColorBrush)Application.Current.Resources[name]).Color;
                foreach (string background in new[] { "BackgroundBrush", "SurfaceBrush", "SelectedBrush" }) foreach (string foreground in new[] { "TextBrush", "MutedBrush", "AccentBrush" }) Check(Theme.Contrast(BrushColor(background), BrushColor(foreground)) >= 4.5, "theme text contrast " + theme.Id);
                foreach (string fill in new[] { "AccentFillBrush", "AccentHoverBrush", "AccentPressedBrush" }) Check(Theme.Contrast(BrushColor(fill), BrushColor("AccentForegroundBrush")) >= 4.5, "theme button contrast " + theme.Id);
                palettes.Add(new { theme.Id, theme.Name, theme.Dark, Colors = Theme.WebColors() });
                main.Width = 900; main.Height = 610; await Task.Delay(80); WindowCheck(main, "main-" + theme.Id);
                var settings = new SettingsWindow(vm); settings.Show(); await Task.Delay(80);
                for (int page = 0; page < 9; page++) { settings.SelectPage(page); settings.Width = 640; settings.Height = 480; await Task.Delay(55); var scroll = UiVerification.FindAll<ScrollViewer>(settings).First(); scroll.ScrollToTop(); await Task.Delay(40); WindowCheck(settings, $"settings-{theme.Id}-{page}"); scroll.ScrollToBottom(); await Task.Delay(40); Capture(settings, $"settings-bottom-{theme.Id}-{page}"); }
                settings.Close();
                foreach (string tab in new[] { "歌词", "歌单", "离线", "定时" }) { var tools = new MusicToolsWindow(vm, tab) { Width = 600, Height = 460 }; tools.Show(); await Task.Delay(70); WindowCheck(tools, $"tools-{theme.Id}-{tab}"); tools.Close(); }
                var info = new InfoWindow(vm); info.Show(); await Task.Delay(60); WindowCheck(info, "info-" + theme.Id); info.Close();
                var remote = new RemoteControlWindow(app.Remote) { Width = 520, Height = 540 }; remote.Show(); await Task.Delay(70); WindowCheck(remote, "remote-" + theme.Id); remote.Close();
                app.ShowMini(); await Task.Delay(60); var mini = Application.Current.Windows.Cast<Window>().OfType<MiniPlayerWindow>().First(); WindowCheck(mini, "mini-" + theme.Id); mini.Hide();
                app.ToggleDesktopLyrics(); await Task.Delay(60); var desktop = Application.Current.Windows.Cast<Window>().OfType<DesktopLyricsWindow>().First(); WindowCheck(desktop, "desktop-" + theme.Id); desktop.Close();
                foreach (var items in new[] { main.FindName("TracksList"), main.FindName("LibraryList"), main.FindName("QueueList") }.OfType<ListBox>()) { var menu = items.ContextMenu!; menu.PlacementTarget = items; menu.IsOpen = true; await Task.Delay(45); Capture(menu, "menu-" + items.Name + "-" + theme.Id); menu.IsOpen = false; }
                app.CaptureTrayMenu(Path.Combine(Store.Root, "tray-" + theme.Id + ".png"));
                Record("themesCompleted", palettes.Count);
            }
            Record("themePalettes", palettes); Record("themeWindows", "PASS");
            Theme.Apply("moon_white"); vm.Settings.Theme = "moon_white"; main.Width = 1040; main.Height = 720; main.Show(); await Task.Delay(100);
            app.Remote.ResetPairing(); Store.Write("remote-qa-access.json", new { Port = app.Remote.Port, Code = app.Remote.PairCode });
            Record("complete", DateTime.UtcNow.ToString("O"));
        }
        catch (Exception error) { Record("failure", error.Message); Store.Log("ERROR", "手机遥控验证失败 " + error.GetType().Name); }
    }
}
