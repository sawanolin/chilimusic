using System.Security.Cryptography;
using System.Text;
namespace ChiliMusic;

public sealed class OfflineCacheService(NavidromeApiClient api, AppSettings settings) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _life = new();
    private List<OfflineEntry> _entries = Store.Read("offline-cache.json", new List<OfflineEntry>());
    private CancellationTokenSource? _download;
    private string DirectoryPath => Path.Combine(Store.Root, "cache", "audio");
    private string Scope => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(api.CacheScope)));
    public event Action? Updated;
    public event Action<string>? Progress;
    public bool Downloading { get; private set; }
    public string? ProtectedPath { get; set; }
    public string? PreparedPath { get; set; }
    public Func<Track, CancellationToken, Task>? CacheExtrasAsync { get; set; }
    public IReadOnlyList<OfflineEntry> Entries => _entries.Where(e => e.Scope == Scope && File.Exists(Path.Combine(DirectoryPath, e.FileName))).ToArray();
    public long Bytes => _entries.Where(e => File.Exists(Path.Combine(DirectoryPath, e.FileName))).Sum(e => e.Size);
    public string? GetPath(Track track)
    {
        var entry = _entries.FirstOrDefault(e => e.Scope == Scope && e.Track.Id == track.Id);
        if (entry == null) return null; var path = Path.Combine(DirectoryPath, entry.FileName);
        if (!File.Exists(path)) return null; entry.LastUsedUtc = DateTime.UtcNow; return path;
    }
    public async Task DownloadAsync(IEnumerable<Track> tracks)
    {
        if (Downloading) throw new ApiException("已有下载任务，请等待完成或取消。");
        if (tracks.Any(t => t.IsNetease)) throw new ApiException("网易云歌曲暂不支持离线缓存，请使用官方客户端下载。");
        var items = tracks.Where(t => !t.IsLocal).DistinctBy(t => t.Id).ToList(); if (items.Count == 0) throw new ApiException("请选择服务器歌曲。");
        Downloading = true; _download = CancellationTokenSource.CreateLinkedTokenSource(_life.Token); var ct = _download.Token; int completed = 0; string activeScope = Scope, apiScope = api.CacheScope;
        Updated?.Invoke();
        try
        {
            foreach (var track in items)
            {
                ct.ThrowIfCancellationRequested(); if (GetPath(track) != null) { completed++; continue; }
                await _gate.WaitAsync(ct);
                string? temp = null;
                try
                {
                    Directory.CreateDirectory(DirectoryPath); string suffix = System.Text.RegularExpressions.Regex.IsMatch(track.Suffix ?? "", "^[a-zA-Z0-9]{1,8}$") ? track.Suffix! : "audio";
                    string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiScope + "\0" + track.Id))); string name = key + "." + suffix; string final = Path.Combine(DirectoryPath, name); temp = final + ".part";
                    if (File.Exists(temp)) File.Delete(temp);
                    DateTime lastReport = DateTime.MinValue;
                    var progress = new Progress<(long Bytes, long? Total)>(p => { if (DateTime.UtcNow - lastReport < TimeSpan.FromMilliseconds(200)) return; lastReport = DateTime.UtcNow; Progress?.Invoke($"{completed + 1}/{items.Count} · {track.Title} · {(p.Total > 0 ? (100d * p.Bytes / p.Total.Value).ToString("0") + "%" : (p.Bytes / 1048576d).ToString("0.0") + " MB")}"); });
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(30));
                    await api.DownloadAsync(track.Id, temp, Math.Max(64, settings.OfflineCacheMb) * 1048576L, progress, timeout.Token);
                    ct.ThrowIfCancellationRequested(); if (Scope != activeScope) throw new ApiException("服务器已切换，请重新下载。");
                    long size = new FileInfo(temp).Length;
                    await TrimCoreAsync(Math.Max(64, settings.OfflineCacheMb) * 1048576L - size, ct);
                    if (Bytes + size > Math.Max(64, settings.OfflineCacheMb) * 1048576L) throw new ApiException("缓存空间不足，正在播放的缓存歌曲暂时无法清理。");
                    File.Move(temp, final, true); temp = null; _entries.RemoveAll(e => e.Scope == Scope && e.Track.Id == track.Id);
                    _entries.Add(new() { Scope = activeScope, Track = track, FileName = name, Size = size }); Persist(); completed++; Updated?.Invoke();
                    if (CacheExtrasAsync != null) try { await CacheExtrasAsync(track, ct); } catch (ApiException) { Store.Log("WARNING", "离线封面或歌词暂时无法获取"); }
                }
                finally { if (temp != null && File.Exists(temp)) File.Delete(temp); _gate.Release(); }
            }
            Progress?.Invoke($"下载完成 · {completed} 首歌曲");
        }
        catch (OperationCanceledException) { Progress?.Invoke("下载已取消或超时"); }
        finally { Downloading = false; _download.Dispose(); _download = null; Persist(); Updated?.Invoke(); }
    }
    public void Cancel() => _download?.Cancel();
    public async Task ClearAsync()
    {
        Cancel(); await _gate.WaitAsync(_life.Token);
        try { await TrimCoreAsync(0, _life.Token); Persist(); Updated?.Invoke(); } finally { _gate.Release(); }
    }
    public async Task EnforceLimitAsync()
    {
        await _gate.WaitAsync(_life.Token); try { await TrimCoreAsync(Math.Max(64, settings.OfflineCacheMb) * 1048576L, _life.Token); Persist(); Updated?.Invoke(); } finally { _gate.Release(); }
    }
    private Task TrimCoreAsync(long limit, CancellationToken ct)
    {
        _entries.RemoveAll(e => !File.Exists(Path.Combine(DirectoryPath, e.FileName))); long size = _entries.Sum(e => e.Size);
        foreach (var entry in _entries.OrderBy(e => e.LastUsedUtc).ToArray())
        {
            ct.ThrowIfCancellationRequested(); if (size <= limit) break; var path = Path.Combine(DirectoryPath, entry.FileName); if (string.Equals(path, ProtectedPath, StringComparison.OrdinalIgnoreCase) || string.Equals(path, PreparedPath, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(path); _entries.Remove(entry); size -= entry.Size; } catch (IOException) { }
        }
        return Task.CompletedTask;
    }
    private void Persist() => Store.Write("offline-cache.json", _entries);
    public void Dispose() { _life.Cancel(); _download?.Cancel(); }
}
