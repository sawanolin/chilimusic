using System.Text.Json;
using System.Windows.Interop;
namespace ChiliMusic;
public static class SelfTest
{
    public static async Task RunAsync(string[] args)
    {
        var settings = Store.Read("config.json", new AppSettings()); var password = CredentialService.Unprotect(settings.ProtectedPassword);
        using var api = new NavidromeApiClient(settings, password); var report = new Dictionary<string, object>();
        if (args.Contains("--features")) { await FeatureVerification.RunCoreAsync(api, settings); return; }
        if (args.Contains("--stress")) { await StressAsync(api, settings); return; }
        void Record(string name, object result) { report[name] = result; Store.Write("selftest.json", report); }
        string marker = "test-password-非ASCII"; if (CredentialService.Unprotect(CredentialService.Protect(marker)) != marker) throw new Exception("DPAPI roundtrip failed"); Record("DPAPI", "PASS");
        Record("ping", await api.PingAsync());
        foreach (var endpoint in new[] { "getMusicFolders", "getIndexes", "getArtists", "getAlbumList2", "getStarred2", "getPlaylists" })
        {
            var parameters = endpoint == "getAlbumList2" ? new (string, string)[] { ("type", "newest"), ("size", "10") } : Array.Empty<(string, string)>();
            var root = await api.CallAsync(endpoint, default, parameters); Record(endpoint, root.GetProperty("status").GetString()!);
        }
        var tracks = await api.RandomAsync(); Record("getRandomSongs", tracks.Count);
        var formats = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks) formats.TryAdd(track.Suffix ?? "unknown", track);
        // Only test songs already exposed by the test account; at most ten bounded random batches.
        for (int i = 0; i < 9 && new[] { "ape", "flac", "mp3", "m4a", "opus" }.Any(f => !formats.ContainsKey(f)); i++) foreach (var track in await api.RandomAsync()) formats.TryAdd(track.Suffix ?? "unknown", track);
        Record("formatsAvailable", formats.Keys.ToArray());
        if (tracks.Count == 0) throw new ApiException("测试音乐库中没有歌曲。");
        var first = tracks[0]; var search = await api.CallAsync("search3", default, ("query", first.Title), ("songCount", "20")); Record("search3", NavidromeApiClient.Tracks(search, "searchResult3").Count);
        var song = await api.CallAsync("getSong", default, ("id", first.Id)); Record("getSong", "PASS");
        bool starred = first.Starred != null;
        try { await api.StarAsync(first.Id, !starred); Record("starOrUnstar", "PASS"); } finally { await api.StarAsync(first.Id, starred); Record("favoriteRestored", "PASS"); }
        using var player = new MpvPlayerService(settings, true); if (args.Contains("--diagnostics")) player.EnableDiagnostics(); Record("mpv", player.Version);
        foreach (var pair in formats.Where(p => new[] { "ape", "flac", "mp3", "m4a", "aac", "opus", "wav", "alac" }.Contains(p.Key)))
        {
            var track = pair.Value; var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ended = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); void Ended(int e) => ended.TrySetResult(e);
            player.Loaded += Loaded; player.Ended += Ended;
            try
            {
                player.Load(api.Url("stream", ("id", track.Id))); var completed = await Task.WhenAny(loaded.Task, ended.Task, Task.Delay(30000));
                if (completed != loaded.Task) { Record("play-" + pair.Key, new { Status = "FAIL", Reason = completed == ended.Task ? "mpv end-file" : "timeout", Idle = player.IsIdle, Pause = player.IsPaused, Position = player.Position, Duration = player.Duration, Events = player.EventTrace }); continue; }
                await Task.Delay(1500); var codec = player.Get("audio-codec-name"); var contentType = await api.ProbeTypeAsync(track.Id, default); var rate = player.Get("audio-params/samplerate"); double before = player.Position;
                player.Pause(); await Task.Delay(250); double paused = player.Position; await Task.Delay(500); bool pauseStable = Math.Abs(player.Position - paused) < 0.15; player.Resume();
                double target = Math.Min(20, Math.Max(1, player.Duration / 3)); player.Seek(target); await Task.Delay(1500); double position = player.Position;
                Record("play-" + pair.Key, new { Status = "PASS", SourceSuffix = track.Suffix, SourceContentType = track.ContentType, ActualContentType = contentType, Codec = codec, SampleRate = rate, PositionBefore = before, PauseStable = pauseStable, SeekTarget = target, SeekActual = position, SeekPassed = Math.Abs(position - target) < 4, OriginalRequest = !api.Url("stream", ("id", track.Id)).Contains("format=") });
            }
            finally { player.Stop(); player.Loaded -= Loaded; player.Ended -= Ended; await Task.Delay(300); }
        }
        var window = new Window(); var hwnd = new WindowInteropHelper(window).EnsureHandle(); using (var media = new MediaSessionService(hwnd)) { await media.UpdateAsync(first, null); media.Status(false, false); Record("SMTC", "PASS"); }
        window.Close();
        Record("completedUtc", DateTime.UtcNow.ToString("O"));
    }
    private static async Task StressAsync(NavidromeApiClient api, AppSettings settings)
    {
        var tracks = new List<Track>(); for (int batch = 0; batch < 5 && tracks.Count < 100; batch++) { tracks.AddRange((await api.RandomAsync()).Where(t => t.Suffix == "flac")); tracks = tracks.DistinctBy(t => t.Id).ToList(); }
        if (tracks.Count == 0) throw new Exception("No FLAC test songs"); using var player = new MpvPlayerService(settings, true); var samples = new List<object>();
        void Sample(int count) { GC.Collect(); GC.WaitForPendingFinalizers(); using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh(); samples.Add(new { Count = count, WorkingSetMb = process.WorkingSet64 / 1048576.0, PrivateMb = process.PrivateMemorySize64 / 1048576.0, Threads = process.Threads.Count, Handles = process.HandleCount }); Store.Write("stress.json", new { Samples = samples, Completed = count, Total = 100 }); }
        Sample(0);
        for (int i = 0; i < 100; i++)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); void Loaded() => loaded.TrySetResult(); player.Loaded += Loaded;
            try { player.Load(api.Url("stream", ("id", tracks[i % tracks.Count].Id))); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)); await Task.Delay(200); } finally { player.Loaded -= Loaded; player.Stop(); await Task.Delay(100); }
            if ((i + 1) % 25 == 0) Sample(i + 1);
        }
        Store.Write("stress.json", new { Status = "PASS", Total = 100, DistinctSongs = Math.Min(100, tracks.Count), Samples = samples });
    }
}
