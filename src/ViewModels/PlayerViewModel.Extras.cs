using System.Collections.ObjectModel;
using System.Windows.Threading;
namespace ChiliMusic;

public sealed partial class PlayerViewModel
{
    public LyricsService Lyrics { get; }
    public ObservableCollection<LyricLine> LyricLines { get; } = [];
    public List<LyricsDocument> LyricDocuments { get; private set; } = [];
    public event Action<LyricLine?>? ActiveLyricChanged;
    private LyricLine? _activeLyric;
    private readonly Dictionary<string, double> _lyricOffsets = Store.Read("lyric-offsets.json", new Dictionary<string, double>());
    private double _lyricOffset;
    public string LyricText => _activeLyric?.Text ?? (LyricLines.Count == 0 ? "暂无歌词" : LyricLines.Any(l => l.Time != null) ? Title : LyricLines[0].Text);
    public string NextLyricText { get; private set; } = "";
    public double LyricOffset { get => _lyricOffset; set { _lyricOffset = Math.Clamp(value, -60, 60); if (Current != null) { _lyricOffsets[Api.CacheScope + "\0" + Current.Id] = _lyricOffset; Store.Write("lyric-offsets.json", _lyricOffsets); } Changed(); UpdateLyricPosition(); } }
    public string LyricsStatus { get; private set; } = "暂无歌词";
    private readonly PlaybackOrder _order = new();
    private int _preparedIndex = -1;
    private readonly DispatcherTimer _sleepTimer = new();
    private DateTime? _sleepDeadline;
    private bool _finishCurrent, _stopAfterCurrent;
    public string SleepStatus => _stopAfterCurrent ? "播完当前歌曲停止" : _sleepDeadline is DateTime deadline ? $"{TimeSpan.FromSeconds(Math.Max(0, (deadline - DateTime.Now).TotalSeconds)).ToString(@"h\:mm\:ss")} 后停止{(_finishCurrent ? " · 等当前歌曲结束" : "")}" : "未设置";
    private void ClearLyrics() { _activeLyric = null; LyricLines.Clear(); LyricDocuments.Clear(); LyricsStatus = "正在加载歌词…"; NextLyricText = ""; Changed(nameof(LyricsStatus)); Changed(nameof(LyricText)); Changed(nameof(NextLyricText)); }
    private async Task LoadLyricsAsync(Track track, int generation, CancellationToken ct)
    {
        var documents = await Lyrics.LoadAsync(track, ct); if (generation != _generation || ct.IsCancellationRequested) return;
        LyricDocuments = documents; _lyricOffset = _lyricOffsets.GetValueOrDefault(Api.CacheScope + "\0" + track.Id); Changed(nameof(LyricOffset)); Changed(nameof(LyricDocuments));
        SetLyricDocument(0);
    }
    public void SetLyricDocument(int index)
    {
        LyricLines.Clear(); _activeLyric = null;
        if (index >= 0 && index < LyricDocuments.Count) foreach (var line in LyricDocuments[index].Lines) LyricLines.Add(line);
        LyricsStatus = LyricLines.Count == 0 ? "暂无歌词" : LyricLines.Any(l => l.Time != null) ? "同步歌词" : "文本歌词";
        Changed(nameof(LyricsStatus)); Changed(nameof(LyricText)); NextLyricText = ""; Changed(nameof(NextLyricText)); UpdateLyricPosition();
    }
    public async Task ImportLyricsAsync(string file)
    {
        var current = Current; int generation = _generation; if (current == null) throw new ApiException("请先选择一首歌曲。");
        if (new FileInfo(file).Length > 1024 * 1024) throw new ApiException("歌词文件超过 1 MB。");
        string text = await LyricsService.ReadTextAsync(file, default); if (generation != _generation) throw new ApiException("歌曲已切换，请重新导入歌词。"); var document = LyricsService.Parse(text); LyricDocuments = [document]; SetLyricDocument(0); Changed(nameof(LyricDocuments));
        var directory = Path.Combine(Store.Root, "cache", "lyrics"); Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Api.CacheScope + "\0" + current.Id))) + ".lrc"), text);
    }
    private void UpdateLyricPosition()
    {
        var line = LyricLines.LastOrDefault(l => l.Time is double time && time <= Position + LyricOffset);
        if (ReferenceEquals(line, _activeLyric)) return; if (_activeLyric != null) _activeLyric.IsActive = false; _activeLyric = line; if (line != null) line.IsActive = true;
        int index = line == null ? -1 : LyricLines.IndexOf(line); NextLyricText = index + 1 < LyricLines.Count ? LyricLines[index + 1].Text : "";
        Changed(nameof(LyricText)); Changed(nameof(NextLyricText)); ActiveLyricChanged?.Invoke(line);
    }
    public void SetSleep(int minutes, bool finishCurrent)
    {
        if (minutes is < 0 or > 720) throw new ApiException("请输入 0–720 分钟，0 表示播完当前歌曲停止。");
        if (minutes == 0 && Player.IsIdle) throw new ApiException("请先播放一首歌曲。");
        _finishCurrent = finishCurrent; _stopAfterCurrent = minutes == 0; _sleepDeadline = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
        if (_stopAfterCurrent) CancelPrepared(); _sleepTimer.Start(); Changed(nameof(SleepStatus));
    }
    public void CancelSleep() { _sleepDeadline = null; _stopAfterCurrent = false; _sleepTimer.Stop(); Changed(nameof(SleepStatus)); PrepareNext(); }
    private void SleepTick(object? sender, EventArgs e)
    {
        if (_sleepDeadline is DateTime deadline && DateTime.Now >= deadline)
        {
            _sleepDeadline = null;
            if (_finishCurrent && !Player.IsIdle) { _stopAfterCurrent = true; CancelPrepared(); }
            else { _sleepTimer.Stop(); Stop(); }
        }
        Changed(nameof(SleepStatus));
    }
    private void CancelPrepared() { _preparedIndex = -1; Offline.PreparedPath = null; if (!_disposed) Player.PrepareNext(null); }
    private void PrepareNext()
    {
        if (!_loaded || Player.IsIdle || _stopAfterCurrent || !Settings.PrefetchNext) { CancelPrepared(); return; }
        int next = _order.PeekNext(Index, Settings.Mode, true);
        if (next < 0 || next == Index) { CancelPrepared(); return; }
        var track = Queue[next]; string? url = track.IsLocal ? File.Exists(track.LocalPath) ? track.LocalPath : null : Offline.GetPath(track) ?? (Api.Configured ? Api.Url("stream", ("id", track.Id)) : null);
        Offline.PreparedPath = track.IsLocal ? null : Offline.GetPath(track); _preparedIndex = url == null ? -1 : next; Player.PrepareNext(url);
    }
    private async Task RecoverDeviceOrNetworkAsync()
    {
        CancelPrepared(); if (Settings.AudioDevice != "auto" && !Player.AudioDevices().Any(d => d.Name == Settings.AudioDevice))
        {
            Settings.AudioDevice = "auto"; Player.Set("audio-device", "auto"); SaveSettings(); Status = "输出设备已断开，改用系统默认设备"; if (Index >= 0) await PlayAsync(Index); return;
        }
        await ReconnectAsync();
    }
}
