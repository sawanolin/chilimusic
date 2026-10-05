namespace ChiliMusic;

internal static class NeteaseVerification
{
    internal static async Task FullAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile(); var report = new Dictionary<string, object>();
        void Record(string name, object value) { report[name] = value; Store.Write("netease-full-verification.json", report); }
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        try
        {
            await UiAsync(app); Check(Store.Read("netease-ui-verification.json", new Dictionary<string, System.Text.Json.JsonElement>())["result"].GetString() == "PASS", "base netease checks");
            var vm = app.Vm; foreach (var login in System.Windows.Application.Current.Windows.OfType<NeteaseAccountWindow>().ToArray()) login.Close();
            await vm.Api.Netease.RefreshAccountAsync(default); Check(vm.Api.Netease.LoggedIn, "saved login after restart"); Record("loginPersistence", "PASS");
            await vm.BrowseAsync("歌单"); Check(vm.Library.Any(i => i.Kind == "playlist"), "account playlists"); Record("playlists", vm.Library.Count);
            await vm.BrowseAsync("收藏"); Check(vm.Results.Count > 0, "account favorites"); Record("favorites", vm.Results.Count);
            await vm.BrowseAsync("每日推荐"); Record("daily", vm.Results.Count);
            await vm.BrowseAsync("最近播放"); Check(vm.Results.Count > 0 && vm.Library.Count == 0, "cloud recent songs"); Record("cloudRecent", vm.Results.Count);
            var match = vm.Current!;
            var original = new Track { Id = "local:lyrics-match-test", LocalPath = Path.Combine(Store.Root, "lyrics-match-test.wav"), Title = match.Title, Artist = match.Artist, Album = match.Album, Duration = match.Duration };
            var documents = await vm.Lyrics.LoadAsync(original, default); Check(documents.Any(d => d.Source.StartsWith("网易云") && d.Lines.Count > 0), "local automatic lyric matching"); Record("localLyricMatching", "PASS");
            var serverTrack = new Track { Id = "server-missing-lyrics-test", Title = match.Title, Artist = match.Artist, Album = match.Album, Duration = match.Duration };
            var serverDocs = await vm.Lyrics.LoadAsync(serverTrack, default); Check(serverDocs.Any(d => d.Source.StartsWith("网易云") && d.Lines.Count > 0), "server missing lyric fallback"); Record("serverLyricFallback", "PASS");
            Check(NeteaseClient.ChooseLyricsMatch(original, [new Track { Title = original.Title + " Live", Artist = original.Artist, Duration = original.Duration }]) == null, "reject different versions");
            Check(NeteaseClient.ChooseLyricsMatch(original, [new Track { Title = original.Title, Artist = "其他歌手", Duration = original.Duration }]) == null, "reject wrong artist");
            Check(NeteaseClient.ChooseLyricsMatch(original, [new Track { Title = original.Title, Artist = original.Artist, Duration = original.Duration + 20 }]) == null, "reject different duration"); Record("matchGuards", "PASS");
            var lrc = Path.Combine(Store.Root, "layout-lyrics.lrc"); await File.WriteAllTextAsync(lrc, "[00:00.00]云把午后的光收好\n[00:03.00]风轻轻走过我的窗\n[00:12.00]把一些温柔留在心上\n[00:15.00]这一句很长很长，需要在窗口变窄时完整换行，不应该超出歌词区域或盖住下方的播放控制按钮。\n[00:28.00]星光点亮回忆的形状\n[00:42.00]让所有遗憾都变得柔软");
            await vm.ImportLyricsAsync(lrc); var longLine = vm.LyricLines[3]; vm.LyricLines[3] = new(longLine.Time, longLine.Text, "A long lyric must remain readable when the window is narrow."); vm.Seek(16); await Task.Delay(200);
            var cases = new List<object>();
            foreach (var palette in ThemeCatalog.All)
            {
                Theme.Apply(palette.Id); var immersive = new ImmersiveWindow(vm) { Width = 860, Height = 600 }; immersive.Show(); await Task.Delay(120);
                var lyricView = UiVerification.FindAll<LyricsView>(immersive).First(); lyricView.Center(); await Task.Delay(80);
                UiVerification.Capture(immersive, Path.Combine(Store.Root, "immersive-" + palette.Id + ".png"));
                immersive.Width = 1100; immersive.Height = 760; await Task.Delay(100); lyricView.Center(); await Task.Delay(80);
                UiVerification.Capture(immersive, Path.Combine(Store.Root, "immersive-full-" + palette.Id + ".png"));
                var tools = new MusicToolsWindow(vm, "歌词") { Width = 600, Height = 460 }; tools.Show(); await Task.Delay(100); UiVerification.Capture(tools, Path.Combine(Store.Root, "lyrics-tools-" + palette.Id + ".png")); tools.Close();
                var desktop = new DesktopLyricsWindow(vm) { Width = 360 }; desktop.Show(); await Task.Delay(60); UiVerification.Capture(desktop, Path.Combine(Store.Root, "lyrics-desktop-" + palette.Id + ".png")); desktop.Close();
                var mini = new MiniPlayerWindow(vm); mini.Show(); await Task.Delay(60); UiVerification.Capture(mini, Path.Combine(Store.Root, "lyrics-mini-" + palette.Id + ".png")); mini.Close();
                var border = (System.Windows.Controls.Border)immersive.Content; Check(border.CornerRadius.TopLeft == 10 && border.Child is System.Windows.FrameworkElement { Clip: System.Windows.Media.RectangleGeometry }, "immersive corners"); immersive.Close(); cases.Add(palette.Id);
            }
            Record("lyricsThemes", cases); Theme.Apply("ink_blue");
            foreach (var file in new[] { vm.Lyrics.CachePath(match), vm.Lyrics.CachePath(match) + ".json" }) if (File.Exists(file)) File.Delete(file);
            await vm.PlayTrackAsync(match); await Task.Delay(2000); vm.PausePlayback();
            await vm.Api.ScrobbleAsync(match.Id, false); await Task.Delay(2500); var recent = await vm.Api.Netease.RecentSongsAsync(default); Check(recent.Any(t => t.Id == match.Id), "recent includes chilimusic playback"); Record("mergedRecent", "PASS");
            var cloud = await vm.Api.Netease.RequestAsync("/api/play-record/song/list", new() { ["limit"] = 100 }); Record("cloudWriteVisible", cloud.GetProperty("data").GetProperty("list").EnumerateArray().Any(row => NavidromeApiClient.Text(row.GetProperty("data"), "id") == "33894312"));
            Record("result", "PASS"); app.ShowImmersive();
            await Task.Delay(300); var finished = System.Windows.Application.Current.Windows.OfType<ImmersiveWindow>().First();
            UiVerification.Capture(finished, Path.Combine(Store.Root, "immersive-playing.png"));
            var picker = new LyricsMatchWindow(vm); picker.Show(); await Task.Delay(2000);
            foreach (var palette in ThemeCatalog.All) { Theme.Apply(palette.Id); await Task.Delay(80); UiVerification.Capture(picker, Path.Combine(Store.Root, "lyrics-match-" + palette.Id + ".png")); }
            picker.Close(); Theme.Apply("ink_blue");
        }
        catch (Exception error) { Record("result", "FAIL"); Record("error", error is ApiException ? error.Message : error.Message); }
    }
    internal static async Task AccountAsync()
    {
        FeatureVerification.RequireIsolatedProfile(); var report = new Dictionary<string, object>();
        try
        {
            using var client = new NeteaseClient(); await client.RefreshAccountAsync(default);
            report["account"] = client.LoggedIn && client.UserId.Length > 0;
            var playlists = await client.SubsonicAsync("getPlaylists", [], default); report["playlists"] = playlists.GetProperty("playlists").GetProperty("playlist").GetArrayLength();
            var likes = await client.SubsonicAsync("getStarred2", [], default); report["favorites"] = likes.GetProperty("starred2").GetProperty("song").GetArrayLength();
            var daily = await client.SubsonicAsync("getDailyRecommendations", [], default); report["daily"] = daily.GetProperty("randomSongs").GetProperty("song").GetArrayLength();
            var recent = await client.RequestAsync("/api/play-record/song/list", new() { ["limit"] = 100 });
            Store.Write("netease-recent-response.json", recent); report["recent"] = recent.GetProperty("data").GetProperty("list").GetArrayLength();
            report["result"] = "PASS";
        }
        catch (Exception error) { report["result"] = "FAIL"; report["error"] = error is ApiException ? error.Message : error.GetType().Name; }
        Store.Write("netease-account-verification.json", report);
    }
    internal static async Task UiAsync(App app)
    {
        FeatureVerification.RequireIsolatedProfile(); var report = new Dictionary<string, object>();
        void Record(string name, object value) { report[name] = value; Store.Write("netease-ui-verification.json", report); }
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        try
        {
            app.Vm.Settings.CatalogSource = "netease"; app.Vm.Volume = 0; app.ShowMain(); await Task.Delay(1000);
            var vm = app.Vm; var main = System.Windows.Application.Current.Windows.OfType<MainWindow>().First();
            await vm.BrowseAsync("搜索", "晴天"); Check(vm.Results.Count > 0 && vm.Results.All(t => t.IsNetease), "netease search mapping"); Record("search", vm.Results.Count);
            var roots = await vm.Api.Netease.SubsonicAsync("getAlbumList2", [("size", "5"), ("offset", "0")], default); Record("newAlbums", roots.GetProperty("albumList2").GetProperty("album").GetArrayLength());
            var details = await vm.Api.Netease.RequestAsync("/api/v3/song/detail", new() { ["c"] = "[{\"id\":33894312}]" });
            var original = details.GetProperty("songs")[0]; var album = original.GetProperty("al");
            var track = new Track { Id = "ncm:33894312", AlbumId = "ncm:" + NavidromeApiClient.Text(album, "id"), Title = NavidromeApiClient.Text(original, "name"), Album = NavidromeApiClient.Text(album, "name"), CoverArt = "ncm-cover:" + NavidromeApiClient.Text(album, "picUrl"), Suffix = "mp3", Duration = original.GetProperty("dt").GetDouble() / 1000, Artist = string.Join(" / ", original.GetProperty("ar").EnumerateArray().Select(a => NavidromeApiClient.Text(a, "name"))) };
            using (var audio = await vm.Api.Netease.OpenAudioAsync(track, vm.Settings.NeteaseQuality, false, "bytes=0-1023", default)) Record("audioHttp", new { Status = (int)audio.StatusCode, Type = audio.Content.Headers.ContentType?.MediaType, Bytes = (await audio.Content.ReadAsByteArrayAsync()).Length });
            vm.Player.EnableDiagnostics(); await vm.PlayTrackAsync(track); for (int i = 0; i < 150 && vm.Player.Get("audio-codec-name").Length == 0; i++) await Task.Delay(100); await Task.Delay(1200);
            Record("nativeDiagnostics", new { Codec = vm.Player.Get("audio-codec-name"), Idle = vm.Player.IsIdle, Status = vm.Status });
            Check(vm.Player.Get("audio-codec-name").Length > 0 && !vm.Player.IsIdle, "native audio decoding"); vm.Seek(10); await Task.Delay(400); vm.PausePlayback();
            Check(Math.Abs(vm.Player.Position - 10) < 2, "netease seek"); Record("playback", new { Codec = vm.Player.Get("audio-codec-name"), Duration = vm.Player.Duration, Seek = true, Quality = vm.Quality, Preview = track.Preview });
            for (int i = 0; i < 60 && vm.Cover == null; i++) await Task.Delay(100); Check(vm.Cover != null, "netease cover"); Record("cover", "PASS");
            var lyrics = await vm.Lyrics.LoadAsync(track, default); Check(lyrics.Any(d => d.Lines.Count > 0), "netease lyrics"); Record("lyrics", "PASS");
            app.Remote.Start(47832); Store.Write("netease-qa-access.json", new { Port = app.Remote.Port, Code = app.Remote.PairCode });
            var cases = new List<object>();
            foreach (var palette in ThemeCatalog.All)
            {
                Theme.Apply(palette.Id); main.Width = 900; main.Height = 610; await Task.Delay(80); UiVerification.Capture(main, Path.Combine(Store.Root, "netease-main-" + palette.Id + ".png"));
                var account = new NeteaseAccountWindow(vm, false) { Width = 460, Height = 460 }; account.Show(); await Task.Delay(60);
                var scroll = UiVerification.FindAll<System.Windows.Controls.ScrollViewer>(account).First(); scroll.ScrollToTop(); await Task.Delay(30); UiVerification.Capture(account, Path.Combine(Store.Root, "netease-account-" + palette.Id + ".png")); scroll.ScrollToBottom(); await Task.Delay(30); UiVerification.Capture(account, Path.Combine(Store.Root, "netease-account-bottom-" + palette.Id + ".png"));
                var border = (System.Windows.Controls.Border)account.Content; Check(border.CornerRadius.TopLeft == 10 && border.Child is System.Windows.FrameworkElement { Clip: System.Windows.Media.RectangleGeometry }, "account corners"); account.Close();
                var settings = new SettingsWindow(vm) { Width = 640, Height = 480 }; settings.Show(); settings.SelectPage(8); await Task.Delay(60); UiVerification.Capture(settings, Path.Combine(Store.Root, "netease-settings-" + palette.Id + ".png")); settings.Close(); cases.Add(palette.Id);
            }
            Record("themeWindows", cases); Theme.Apply("moon_white"); main.Width = 1040; main.Height = 720; main.Hide(); app.ShowNetease(); await Task.Delay(1500);
            var login = System.Windows.Application.Current.Windows.OfType<NeteaseAccountWindow>().First(); UiVerification.Capture(login, Path.Combine(Store.Root, "netease-login.png"));
            Record("result", "PASS"); Record("readyForScan", true);
        }
        catch (Exception error) { Record("result", "FAIL"); Record("error", error is ApiException ? error.Message : error.Message); }
    }
    internal static async Task NetworkAsync()
    {
        FeatureVerification.RequireIsolatedProfile(); var report = new Dictionary<string, object>();
        try
        {
            using var client = new NeteaseClient();
            var key = await client.CreateLoginKeyAsync(default); report["qrKey"] = key.Length > 0;
            var search = await client.RequestAsync("/api/cloudsearch/pc", new() { ["s"] = "晴天", ["type"] = 1, ["limit"] = 3, ["offset"] = 0, ["total"] = true });
            report["search"] = search.TryGetProperty("result", out var results) && results.TryGetProperty("songs", out var songs) ? songs.GetArrayLength() : 0;
            var stream = await client.RequestAsync("/api/song/enhance/player/url/v1", new() { ["ids"] = "[33894312]", ["level"] = "standard", ["encodeType"] = "mp3" }, eapi: true);
            var entry = stream.GetProperty("data")[0]; report["streamAvailable"] = !string.IsNullOrEmpty(NavidromeApiClient.Text(entry, "url")); report["streamCode"] = NavidromeApiClient.Text(entry, "code"); report["result"] = "PASS";
        }
        catch (Exception error) { report["result"] = "FAIL"; report["error"] = error is ApiException ? error.Message : error.GetType().Name; }
        Store.Write("netease-network-verification.json", report);
    }
}
