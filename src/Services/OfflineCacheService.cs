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
    public System.Collections.ObjectModel.ObservableCollection<DownloadTask> Tasks { get; } = [];
    private Task? _worker;
    private DownloadTask? _active;
    public Task DownloadAsync(IEnumerable<Track> tracks)
    {
        var source = tracks.ToList();
        if (source.Any(t => t.IsNetease)) throw new ApiException("网易云歌曲暂不支持离线缓存，请使用官方客户端下载。");
        var songs = source.Where(t => !t.IsLocal).DistinctBy(t => t.Id).ToArray();
        if (songs.Length == 0) throw new ApiException("请选择服务器歌曲。");
        foreach (var track in songs)
        {
            var existing = Tasks.FirstOrDefault(t => t.Scope == Scope && t.Track.Id == track.Id && t.State != "已取消");
            if (existing != null) { if (existing.State is "失败" or "已暂停") existing.State = "等待"; existing.Refresh(); continue; }
            Tasks.Add(new() { Track = track, Scope = Scope });
        }
        Updated?.Invoke();
        return StartWorker();
    }
    private Task StartWorker()
    {
        if (_worker is { IsCompleted: false }) return _worker;
        return _worker = WorkAsync();
    }
    private async Task WorkAsync()
    {
        Downloading = true; Updated?.Invoke();
        try
        {
            while (!_life.IsCancellationRequested)
            {
                var job = Tasks.FirstOrDefault(t => t.State == "等待" && t.Scope == Scope); if (job == null) break;
                _active = job; _download = CancellationTokenSource.CreateLinkedTokenSource(_life.Token); var ct = _download.Token;
                job.State = "下载中"; job.Error = ""; job.Refresh();
                await _gate.WaitAsync(_life.Token);
                try
                {
                    string? cached = GetPath(job.Track);
                    if (cached != null) { SetPinned(job.Track, job.Pinned); job.State = "完成"; continue; }
                    Directory.CreateDirectory(DirectoryPath);
                    string suffix = System.Text.RegularExpressions.Regex.IsMatch(job.Track.Suffix ?? "", "^[a-zA-Z0-9]{1,8}$") ? job.Track.Suffix! : "audio";
                    string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(api.CacheScope + "\0" + job.Track.Id))) + "." + suffix;
                    string final = Path.Combine(DirectoryPath, name), temp = final + ".part";
                    var progress = new Progress<(long Bytes, long? Total)>(value => { job.Bytes = value.Bytes; job.Total = value.Total; job.Refresh(); Progress?.Invoke(job.Label); });
                    await api.DownloadAsync(job.Track.Id, temp, Math.Max(64, settings.OfflineCacheMb) * 1048576L, progress, ct);
                    ct.ThrowIfCancellationRequested(); if (job.Scope != Scope) throw new ApiException("服务器已切换，请重新下载。");
                    long size = new FileInfo(temp).Length;
                    await TrimCoreAsync(Math.Max(64, settings.OfflineCacheMb) * 1048576L - size, ct);
                    if (Bytes + size > Math.Max(64, settings.OfflineCacheMb) * 1048576L) throw new ApiException("空间不足：请增加容量或取消部分下载的保留状态。");
                    File.Move(temp, final, true); _entries.RemoveAll(e => e.Scope == Scope && e.Track.Id == job.Track.Id);
                    _entries.Add(new() { Scope = Scope, Track = job.Track, FileName = name, Size = size, Pinned = job.Pinned }); Persist(); job.State = "完成"; job.Bytes = size; job.Total = size;
                    if (CacheExtrasAsync != null) try { await CacheExtrasAsync(job.Track, ct); } catch (Exception error) when (error is ApiException or OperationCanceledException) { Store.Log("WARNING", "离线附件暂未完成"); }
                }
                catch (OperationCanceledException) { if (!ct.IsCancellationRequested) { job.State = "失败"; job.Error = "连接超时，可重试续传"; } else if (job.State is not ("已取消" or "等待")) job.State = "已暂停"; }
                catch (Exception error) when (error is ApiException or IOException or HttpRequestException or UnauthorizedAccessException) { job.State = "失败"; job.Error = error is ApiException ? error.Message : "下载未完成，可重试续传"; }
                finally { if (job.State == "已取消") DeletePartial(job); _gate.Release(); _download.Dispose(); _download = null; _active = null; job.Refresh(); Updated?.Invoke(); }
            }
        }
        finally { Downloading = false; Persist(); Updated?.Invoke(); Progress?.Invoke("下载任务已处理，可继续或重试未完成项目"); }
    }
    public void Pause(DownloadTask job) { if (job.State is not ("等待" or "下载中")) return; job.State = "已暂停"; if (ReferenceEquals(job, _active)) _download?.Cancel(); job.Refresh(); }
    public void Resume(DownloadTask job) { if (job.State is not ("失败" or "已暂停" or "已取消")) return; job.State = "等待"; job.Error = ""; job.Refresh(); _ = StartWorker(); }
    public void Cancel(DownloadTask job) { job.State = "已取消"; if (ReferenceEquals(job, _active)) _download?.Cancel(); else DeletePartial(job); job.Refresh(); }
    private void DeletePartial(DownloadTask job)
    {
        if (job.Scope != Scope) return;
        string suffix = System.Text.RegularExpressions.Regex.IsMatch(job.Track.Suffix ?? "", "^[a-zA-Z0-9]{1,8}$") ? job.Track.Suffix! : "audio";
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(api.CacheScope + "\0" + job.Track.Id))) + "." + suffix + ".part";
        try { File.Delete(Path.Combine(DirectoryPath, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Cancel() { foreach (var job in Tasks.Where(t => t.State is "下载中" or "等待")) Pause(job); }
    public void RetryFailed() { foreach (var job in Tasks.Where(t => t.State is "失败" or "已暂停")) { job.State = "等待"; job.Refresh(); } _ = StartWorker(); }
    public void SetPinned(Track track, bool pinned) { var entry = _entries.FirstOrDefault(e => e.Scope == Scope && e.Track.Id == track.Id); if (entry != null) { entry.Pinned = pinned; Persist(); Updated?.Invoke(); } }
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
            ct.ThrowIfCancellationRequested(); if (size <= limit) break; var path = Path.Combine(DirectoryPath, entry.FileName); if (entry.Pinned || string.Equals(path, ProtectedPath, StringComparison.OrdinalIgnoreCase) || string.Equals(path, PreparedPath, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(path); _entries.Remove(entry); size -= entry.Size; } catch (IOException) { }
        }
        return Task.CompletedTask;
    }
    private void Persist() => Store.Write("offline-cache.json", _entries);
    public void Dispose() { _life.Cancel(); _download?.Cancel(); }
}
