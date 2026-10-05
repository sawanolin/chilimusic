using System.Security.Cryptography;
using System.Text;
namespace ChiliMusic;

public sealed class LocalLibraryService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _debounce;
    private List<Track> _tracks = [];
    private readonly object _tracksLock = new(), _watchLock = new();
    private Task? _initialization;
    public event Action<IReadOnlyList<Track>>? Updated;
    public event Action<string>? Progress;
    public IReadOnlyList<Track> Snapshot { get { lock (_tracksLock) return _tracks.ToArray(); } }
    public LocalLibraryService(AppSettings settings) => _settings = settings;
    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        var tracks = await Task.Run(() => Store.Read("local-library.json", new List<Track>()), _lifetime.Token);
        lock (_tracksLock) _tracks = tracks;
        Updated?.Invoke(Snapshot); RebuildWatchers();
        if (_settings.MusicFolders.Count > 0) await ScanCoreAsync();
    }
    public async Task ImportAsync(IEnumerable<string> paths)
    {
        await InitializeAsync();
        await _scanGate.WaitAsync(_lifetime.Token);
        try
        {
            var files = paths.Select(Path.GetFullPath).Where(File.Exists).Where(IsMusic).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await Task.Run(() => Merge(files, false), _lifetime.Token); await PersistAsync(); Updated?.Invoke(Snapshot);
        }
        finally { _scanGate.Release(); }
    }
    public async Task AddFolderAsync(string path)
    {
        path = Path.GetFullPath(path); if (!Directory.Exists(path)) throw new ApiException("找不到这个音乐文件夹。");
        if (!_settings.MusicFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) _settings.MusicFolders.Add(path);
        Store.Write("config.json", _settings); RebuildWatchers(); await ScanAsync();
    }
    public void RemoveFolder(string path)
    {
        _settings.MusicFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); Store.Write("config.json", _settings); RebuildWatchers();
    }
    public async Task ScanAsync()
    {
        await InitializeAsync(); await ScanCoreAsync();
    }
    private async Task ScanCoreAsync()
    {
        await _scanGate.WaitAsync(_lifetime.Token);
        try
        {
            var roots = _settings.MusicFolders.ToArray();
            await Task.Run(() => Merge(roots.SelectMany(EnumerateMusic).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), true, roots), _lifetime.Token);
            await PersistAsync(); Updated?.Invoke(Snapshot); Progress?.Invoke($"已整理 {Snapshot.Count(t => !t.Missing)} 首本地音乐"); RebuildWatchers();
        }
        finally { _scanGate.Release(); }
    }
    private void Merge(List<string> files, bool markMissing, string[]? roots = null)
    {
        lock (_tracksLock)
        {
        var byPath = _tracks.Where(t => t.LocalPath != null).GroupBy(t => t.LocalPath!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byFingerprint = _tracks.Where(t => t.Fingerprint.Length > 0).GroupBy(t => t.Fingerprint).ToDictionary(g => g.Key, g => g.First());
        if (markMissing) foreach (var t in _tracks.Where(t => t.LocalPath != null && (roots ?? []).Any(root => Within(t.LocalPath!, root)))) t.Missing = !File.Exists(t.LocalPath);
        int count = 0;
        foreach (var path in files)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path); byPath.TryGetValue(path, out var old);
                if (old != null && old.FileSize == info.Length && old.FileModifiedUtc == info.LastWriteTimeUtc && old.Fingerprint.StartsWith("sha256:")) { old.Missing = false; continue; }
                var fresh = ReadTrack(path);
                if (old == null && byFingerprint.TryGetValue(fresh.Fingerprint, out var same))
                {
                    if (!File.Exists(same.LocalPath)) old = same; else continue;
                }
                if (old != null) { fresh.Id = old.Id; fresh.Starred = old.Starred; int index = _tracks.IndexOf(old); _tracks[index] = fresh; }
                else _tracks.Add(fresh);
                byPath[path] = fresh; byFingerprint[fresh.Fingerprint] = fresh;
                if (++count % 25 == 0) Progress?.Invoke($"正在整理本地音乐 · {count} 首");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Store.Log("WARNING", "音乐文件暂时无法读取"); }
        }
        }
    }
    public static Track ReadTrack(string path)
    {
        path = Path.GetFullPath(path); var info = new FileInfo(path);
        var track = new Track { Id = "local:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))), LocalPath = path, Title = Path.GetFileNameWithoutExtension(path), Artist = "本地音乐", Album = "本地音乐", Suffix = info.Extension.TrimStart('.'), FileSize = info.Length, FileModifiedUtc = info.LastWriteTimeUtc, Fingerprint = Fingerprint(path) };
        try
        {
            using var file = TagLib.File.Create(path); var tag = file.Tag;
            if (!string.IsNullOrWhiteSpace(tag.Title)) track.Title = tag.Title;
            if (tag.Performers.Length > 0) track.Artist = string.Join(" / ", tag.Performers);
            if (!string.IsNullOrWhiteSpace(tag.Album)) track.Album = tag.Album;
            track.TrackNumber = tag.Track; track.DiscNumber = tag.Disc; track.Year = tag.Year; track.Genre = string.Join(" / ", tag.Genres);
            track.Duration = file.Properties.Duration.TotalSeconds; track.BitRate = file.Properties.AudioBitrate; track.SamplingRate = file.Properties.AudioSampleRate; track.ChannelCount = file.Properties.AudioChannels; track.BitDepth = file.Properties.BitsPerSample;
            track.EmbeddedLyrics = tag.Lyrics is { Length: <= 262144 } ? tag.Lyrics : null;
            var picture = tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? tag.Pictures.FirstOrDefault();
            if (picture != null && picture.Data.Count is > 0 and <= 10485760)
            {
                var bytes = picture.Data.Data; var key = Convert.ToHexString(SHA256.HashData(bytes)); var dir = Path.Combine(Store.Root, "cache", "local-covers"); Directory.CreateDirectory(dir);
                var cover = Path.Combine(dir, key + ".img"); if (!File.Exists(cover)) File.WriteAllBytes(cover, bytes); track.EmbeddedCoverPath = cover;
            }
        }
        catch (Exception e) when (e is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or ArgumentException or NotSupportedException) { }
        track.EmbeddedCoverPath ??= new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }.Select(name => Path.Combine(info.DirectoryName!, name)).FirstOrDefault(File.Exists);
        return track;
    }
    private static string Fingerprint(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(file));
    }
    public static bool IsMusic(string path) => PlayerViewModel.LocalExtensions.Contains(Path.GetExtension(path));
    private static bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<string> EnumerateMusic(string root)
    {
        var pending = new Stack<string>(); if (Directory.Exists(root)) pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            string[] files; string[] dirs;
            try { files = Directory.GetFiles(folder); dirs = Directory.GetDirectories(folder); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files.Where(IsMusic)) yield return file;
            foreach (var dir in dirs) try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) pending.Push(dir); } catch (IOException) { }
        }
    }
    public Task PersistAsync() { var snapshot = Snapshot; return Task.Run(() => Store.Write("local-library.json", snapshot), _lifetime.Token); }
    private void RebuildWatchers()
    {
        lock (_watchLock)
        {
        if (_lifetime.IsCancellationRequested) return;
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        foreach (var root in _settings.MusicFolders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            try { var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName, EnableRaisingEvents = true }; watcher.Created += OnChanged; watcher.Changed += OnChanged; watcher.Deleted += OnChanged; watcher.Renamed += OnChanged; watcher.Error += (_, _) => DebounceScan(); _watchers.Add(watcher); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs e) => DebounceScan();
    private void DebounceScan()
    {
        lock (_watchLock)
        {
        if (_lifetime.IsCancellationRequested) return;
        _debounce?.Cancel(); _debounce?.Dispose(); _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); var token = _debounce.Token;
        _ = Task.Run(async () => { try { await Task.Delay(1500, token); await ScanAsync(); } catch (OperationCanceledException) { } catch (Exception e) { Store.Log("WARNING", "音乐目录更新失败 " + e.GetType().Name); } }, token);
        }
    }
    public void Dispose() { lock (_watchLock) { _lifetime.Cancel(); _debounce?.Cancel(); foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); } }
}
