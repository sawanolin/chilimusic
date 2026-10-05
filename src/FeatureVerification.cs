using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ChiliMusic;

internal static class FeatureVerification
{
    internal static void RequireIsolatedProfile()
    {
        string normal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "chilimusic");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHILIMUSIC_DATA")) || Path.GetFullPath(Store.Root).TrimEnd('\\').Equals(Path.GetFullPath(normal).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Feature verification requires CHILIMUSIC_DATA pointing to a separate temporary directory.");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    public static async Task RunCoreAsync(NavidromeApiClient api, AppSettings source)
    {
        RequireIsolatedProfile();
        var results = new List<string>(); void Passed(string name) { results.Add(name); Store.Write("feature-core.json", new { Passed = results, Complete = false }); }
        var order = new PlaybackOrder(); order.Reset(24); order.Record(0); var round = new List<int> { 0 };
        for (int i = 0; i < 23; i++) round.Add(order.Next(round[^1], PlayMode.Shuffle, false));
        Check(round.Distinct().Count() == 24, "shuffle round repeats"); int previous = round[^1]; int next = order.Next(previous, PlayMode.Shuffle, false); Check(next != previous, "shuffle boundary repeats");
        Check(order.Previous() == previous && order.Next(previous, PlayMode.Shuffle, false) == next, "actual previous/forward history");
        var state = new QueueState { History = order.History.ToList(), HistoryPosition = order.Cursor, ShuffleRemaining = order.Remaining.ToList() }; var restored = new PlaybackOrder(); restored.Restore(24, state); Check(restored.PeekNext(next, PlayMode.Shuffle, false) == order.PeekNext(next, PlayMode.Shuffle, false), "order resume");
        restored.Remap(i => i == next ? -1 : i > next ? i - 1 : i, 23); Check(restored.History.All(i => i >= 0 && i < 23), "removed queue history"); Passed("Shuffle, history, queue edits and resume");
        var lrc = LyricsService.Parse("[ar:歌手]\n[offset:250]\n[00:01.50][00:03.00]第一行\n[00:04,25]第二行"); Check(lrc.Lines.Count == 3 && lrc.Lines[0].Time == 1.75 && lrc.Lines[2].Time == 4.5 && lrc.Lines[0].Text == "第一行", "LRC timestamps/offset");
        Check(LyricsService.Parse("第一行\n第二行").Lines.All(l => l.Time == null), "plain lyrics"); Passed("LRC and plain lyrics");
        string directory = Path.Combine(Store.Root, "feature-fixtures"); Directory.CreateDirectory(directory); string file = Path.Combine(directory, "first.wav"), second = Path.Combine(directory, "second.wav");
        WriteWave(file, 2, 220); WriteWave(second, 3, 440);
        var pixels = Enumerable.Repeat(new byte[] { 40, 80, 210, 255 }, 256).SelectMany(p => p).ToArray(); var picture = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture)); using var png = new MemoryStream(); encoder.Save(png);
        using (var tagged = TagLib.File.Create(file)) { tagged.Tag.Title = "标签标题"; tagged.Tag.Performers = ["标签歌手"]; tagged.Tag.Album = "标签专辑"; tagged.Tag.Track = 3; tagged.Tag.Disc = 2; tagged.Tag.Year = 2025; tagged.Tag.Lyrics = "[00:00.00]内嵌歌词"; tagged.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(png.ToArray())) { Type = TagLib.PictureType.FrontCover }]; tagged.Save(); }
        var track = LocalLibraryService.ReadTrack(file); Check(track.Title == "标签标题" && track.Artist == "标签歌手" && track.TrackNumber == 3 && track.DiscNumber == 2 && track.SamplingRate == 44100 && track.BitDepth == 16 && File.Exists(track.EmbeddedCoverPath), "local metadata/cover"); Passed("Local tags and embedded cover");
        var settings = new AppSettings { TaskbarEnabled = false }; using var library = new LocalLibraryService(settings); await library.InitializeAsync(); await library.ImportAsync([file, file, second]); Check(library.Snapshot.Count(t => !t.Missing) == 2, "path dedup");
        string nested = Path.Combine(directory, "sub"); Directory.CreateDirectory(nested); string copy = Path.Combine(nested, "copy.wav"); File.Copy(file, copy, true); await library.AddFolderAsync(directory); Check(library.Snapshot.Count(t => !t.Missing) == 2, "recursive content dedup");
        string moved = Path.Combine(directory, "renamed.wav"); File.Move(second, moved); await library.ScanAsync(); Check(library.Snapshot.Any(t => t.LocalPath == moved && !t.Missing) && library.Snapshot.Count(t => !t.Missing) == 2, "moved file tracking");
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); library.Updated += list => { if (list.Any(t => t.LocalPath == moved && t.Missing)) seen.TrySetResult(); }; File.Delete(moved); await seen.Task.WaitAsync(TimeSpan.FromSeconds(12)); Passed("Directory recursion, duplicate, rename and watcher delete");
        File.WriteAllText(Path.ChangeExtension(file, ".lrc"), "[00:00.00]文件歌词\n[00:01.00]第二行"); var lyrics = new LyricsService(api); var docs = await lyrics.LoadAsync(track, default); Check(docs.First().Lines[0].Text == "文件歌词", "sidecar priority"); Passed("Sidecar lyrics");
        using var offline = new OfflineCacheService(api, new AppSettings()); var playlists = new PlaylistService(); var playlist = playlists.Create("本地测试", [track]); string m3u = Path.Combine(directory, "roundtrip.m3u8"); await playlists.ExportAsync(playlist, m3u, offline); var imported = await PlaylistService.ImportPathsAsync(m3u); Check(imported.SequenceEqual(new[] { file }), "M3U8 roundtrip"); playlists.Local.Remove(playlist); playlists.Save(); Passed("Local playlist and M3U8 roundtrip");
        using (var native = new MpvPlayerService(new AppSettings { ReplayGain = "album", ReplayGainPreamp = 2, PrefetchNext = true }, true))
        {
            Check(native.Get("replaygain") == "album" && native.Get("replaygain-clip") == "no" && native.Get("prefetch-playlist") == "yes", "native gain/prefetch options");
            Check(native.AudioDevices().Any(d => d.Name == "auto"), "native audio device node");
            native.Load(file, .5); await UntilAsync(() => native.Get("audio-codec-name").Length > 0, 10); native.PrepareNext(file);
            await UntilAsync(() => native.Get("playlist-pos") == "1", 10); Check(native.Position < .8, "prefetch start offset leaked"); native.Pause(); double position = native.Position; await Task.Delay(350); Check(Math.Abs(native.Position - position) < .1, "native pause"); native.Set("replaygain", "track"); native.Set("audio-device", "auto"); Check(native.Get("replaygain") == "track", "gain update");
        }
        Passed("Native next-track transition, resume start, pause, output and ReplayGain");
        Theme.Apply("Light"); Theme.SetCoverAccent(picture, true); var color = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color; Theme.SetCoverAccent(null, false); Check(((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color != color, "accent reset"); Passed("Cover accent reset");
        if (api.Configured) await RunServerAsync(api, source, Passed);
        Store.Write("feature-core.json", new { Passed = results, Complete = true, CompletedUtc = DateTime.UtcNow });
    }
    private static async Task RunServerAsync(NavidromeApiClient api, AppSettings source, Action<string> passed)
    {
        await api.PingAsync(); var songs = await api.RandomAsync(); Check(songs.Count > 0, "server has no songs"); var song = songs.First();
        string? id = null;
        try
        {
            id = await api.CreatePlaylistAsync("chilimusic 功能测试 " + Guid.NewGuid().ToString("N")[..8], [song.Id]); Check(id.Length > 0, "server playlist id");
            await api.UpdatePlaylistAsync(id, "chilimusic 测试歌单", add: [song.Id]); var list = await api.CallAsync("getPlaylist", default, ("id", id)); Check(NavidromeApiClient.Tracks(list, "playlist", "entry").Count == 2, "server playlist add"); await api.UpdatePlaylistAsync(id, remove: [1]); list = await api.CallAsync("getPlaylist", default, ("id", id)); Check(NavidromeApiClient.Tracks(list, "playlist", "entry").Count == 1, "server playlist remove"); passed("Server playlist create, rename, add and remove");
        }
        finally { if (!string.IsNullOrEmpty(id)) await api.CallAsync("deletePlaylist", default, ("id", id)); }
        using var cache = new OfflineCacheService(api, new AppSettings { OfflineCacheMb = 256 }); await cache.DownloadAsync([song]); string path = cache.GetPath(song) ?? ""; Check(File.Exists(path) && new FileInfo(path).Length > 0, "offline download");
        using (var native = new MpvPlayerService(new(), true)) { native.Load(path); await UntilAsync(() => native.Get("audio-codec-name").Length > 0, 10); Check(native.Duration > 0, "cached decode"); }
        cache.ProtectedPath = path; await cache.ClearAsync(); Check(File.Exists(path), "playing file evicted"); cache.ProtectedPath = null; await cache.ClearAsync(); Check(cache.Bytes == 0 && !File.Exists(path), "cache clear"); passed("Real song download, local decode and protected cache eviction");
        if (songs.Count > 1) { var download = cache.DownloadAsync([songs[1]]); await Task.Delay(30); cache.Cancel(); await download; Check(!cache.Downloading && !Directory.EnumerateFiles(Path.Combine(Store.Root, "cache", "audio"), "*.part").Any(), "cancel left partial file"); await cache.ClearAsync(); passed("Download cancellation and partial-file cleanup"); }
        string audio = Path.Combine(Store.Root, "cache", "audio"); string a = Path.Combine(audio, "capacity-a.audio"), b = Path.Combine(audio, "capacity-b.audio"); using (var f = File.Create(a)) f.SetLength(40L * 1048576); using (var f = File.Create(b)) f.SetLength(40L * 1048576);
        string scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(api.CacheScope))); Store.Write("offline-cache.json", new[] { new OfflineEntry { Scope = scope, Track = new() { Id = "capacity-a" }, FileName = "capacity-a.audio", Size = 40L * 1048576, LastUsedUtc = DateTime.UtcNow.AddDays(-2) }, new OfflineEntry { Scope = scope, Track = new() { Id = "capacity-b" }, FileName = "capacity-b.audio", Size = 40L * 1048576, LastUsedUtc = DateTime.UtcNow.AddDays(-1) } });
        using (var limited = new OfflineCacheService(api, new() { OfflineCacheMb = 64 })) { limited.ProtectedPath = a; await limited.EnforceLimitAsync(); Check(limited.Bytes <= 64L * 1048576 && File.Exists(a) && !File.Exists(b), "bounded cache eviction"); limited.ProtectedPath = null; await limited.ClearAsync(); } passed("Capacity limit and protected-file eviction");
        var lyrics = new LyricsService(api); await lyrics.LoadAsync(song, default); passed("Server lyrics endpoint and fallback");
    }
    private static async Task UntilAsync(Func<bool> condition, int seconds) { var end = DateTime.UtcNow.AddSeconds(seconds); while (!condition()) { if (DateTime.UtcNow >= end) throw new TimeoutException(); await Task.Delay(40); } }
    internal static void WriteWave(string file, int seconds, double frequency)
    {
        const int rate = 44100; int bytes = seconds * rate * 2; using var stream = File.Create(file); using var writer = new BinaryWriter(stream); writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); for (int i = 0; i < rate * seconds; i++) writer.Write((short)(300 * Math.Sin(2 * Math.PI * frequency * i / rate)));
    }
}
