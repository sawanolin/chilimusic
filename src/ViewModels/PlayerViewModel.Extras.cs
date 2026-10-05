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
    public string TranslationText => _activeLyric?.Translation ?? "";
    private bool _showLyricTranslation = true;
    public bool ShowLyricTranslation { get => _showLyricTranslation; set { _showLyricTranslation = value; Changed(); } }
    public int LyricDocumentIndex { get; private set; }
    public int LyricsRevision { get; private set; }
    public double LyricOffset { get => _lyricOffset; set { _lyricOffset = Math.Clamp(value, -60, 60); if (Current != null) { _lyricOffsets[Api.ScopeForId(Current.Id) + "\0" + Current.Id] = _lyricOffset; Store.Write("lyric-offsets.json", _lyricOffsets); } Changed(); UpdateLyricPosition(); } }
    public string LyricsStatus { get; private set; } = "暂无歌词";
    private readonly PlaybackOrder _order = new();
    private int _preparedIndex = -1;
    private readonly DispatcherTimer _sleepTimer = new();
    private DateTime? _sleepDeadline;
    private bool _finishCurrent, _stopAfterCurrent;
    public string SleepStatus => _stopAfterCurrent ? "播完当前歌曲停止" : _sleepDeadline is DateTime deadline ? $"{TimeSpan.FromSeconds(Math.Max(0, (deadline - DateTime.Now).TotalSeconds)).ToString(@"h\:mm\:ss")} 后停止{(_finishCurrent ? " · 等当前歌曲结束" : "")}" : "未设置";
    private void ClearLyrics() { _activeLyric = null; LyricLines.Clear(); LyricDocuments = []; LyricsRevision++; LyricsStatus = "正在加载歌词…"; NextLyricText = ""; Changed(nameof(LyricDocuments)); Changed(nameof(LyricsStatus)); Changed(nameof(LyricText)); Changed(nameof(TranslationText)); Changed(nameof(NextLyricText)); }
    private async Task LoadLyricsAsync(Track track, int generation, CancellationToken ct)
    {
        List<LyricsDocument> documents;
        try { documents = await Lyrics.LoadAsync(track, ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { documents = []; Store.Log("WARNING", $"歌词加载失败 {error.GetType().Name}"); }
        if (generation != _generation || ct.IsCancellationRequested) return;
        LyricDocuments = documents; _lyricOffset = _lyricOffsets.GetValueOrDefault(Api.ScopeForId(track.Id) + "\0" + track.Id); Changed(nameof(LyricOffset)); Changed(nameof(LyricDocuments));
        SetLyricDocument(0);
    }
    public void SetLyricDocument(int index)
    {
        LyricDocumentIndex = index; LyricsRevision++;
        LyricLines.Clear(); _activeLyric = null;
        if (index >= 0 && index < LyricDocuments.Count)
        {
            var document = LyricDocuments[index]; var translated = index == 0 ? LyricDocuments.FirstOrDefault(d => d.Language == "翻译")?.Lines : null;
            foreach (var line in document.Lines) { var translation = line.Time is double time ? translated?.FirstOrDefault(t => t.Time is double other && Math.Abs(time - other) < .15)?.Text ?? "" : ""; LyricLines.Add(new(line.Time, line.Text, translation)); }
        }
        LyricsStatus = LyricLines.Count == 0 ? "暂无歌词" : LyricLines.Any(l => l.Time != null) ? "同步歌词" : "文本歌词";
        if (index >= 0 && index < LyricDocuments.Count && LyricDocuments[index].Source.Length > 0) LyricsStatus += " · " + LyricDocuments[index].Source;
        Changed(nameof(LyricDocumentIndex)); Changed(nameof(TranslationText));
        Changed(nameof(LyricsStatus)); Changed(nameof(LyricText)); NextLyricText = ""; Changed(nameof(NextLyricText)); UpdateLyricPosition();
    }
    public async Task ImportLyricsAsync(string file)
    {
        var current = Current; int generation = _generation; if (current == null) throw new ApiException("请先选择一首歌曲。");
        if (new FileInfo(file).Length > 1024 * 1024) throw new ApiException("歌词文件超过 1 MB。");
        string text = await LyricsService.ReadTextAsync(file, default); if (generation != _generation) throw new ApiException("歌曲已切换，请重新导入歌词。"); var document = LyricsService.Parse(text); LyricDocuments = [document]; SetLyricDocument(0); Changed(nameof(LyricDocuments));
        var directory = Path.Combine(Store.Root, "cache", "lyrics"); Directory.CreateDirectory(directory); string path = Lyrics.CachePath(current); if (File.Exists(path + ".match.json")) File.Delete(path + ".match.json"); await File.WriteAllTextAsync(path, text);
    }
    private void UpdateLyricPosition()
    {
        var line = LyricLines.LastOrDefault(l => l.Time is double time && time <= Position + LyricOffset);
        if (ReferenceEquals(line, _activeLyric)) return; if (_activeLyric != null) _activeLyric.IsActive = false; _activeLyric = line; if (line != null) line.IsActive = true;
        int index = line == null ? -1 : LyricLines.IndexOf(line); NextLyricText = index + 1 < LyricLines.Count ? LyricLines[index + 1].Text : "";
        Changed(nameof(LyricText)); Changed(nameof(NextLyricText)); ActiveLyricChanged?.Invoke(line);
        Changed(nameof(TranslationText));
    }
    public async Task ApplyLyricsMatchAsync(Track match)
    {
        var current = Current; int generation = _generation; if (current == null) throw new ApiException("请先播放一首歌曲。");
        var docs = await Lyrics.ApplyMatchAsync(current, match, _playCts.Token); if (generation != _generation) return;
        LyricDocuments = docs; Changed(nameof(LyricDocuments)); SetLyricDocument(0);
    }
    private void OnNeteaseAccountChanged()
    {
        void Apply()
        {
            if (_disposed) return;
            foreach (var track in Queue.Concat(Results).Where(t => t.IsNetease).Distinct()) { track.Starred = Api.Netease.IsLiked(track.Id) == true ? "liked" : null; track.NotifyMetadata(); }
            RefreshTrack();
            if (Current is { } current && LyricLines.Count == 0 && Api.Netease.LoggedIn) Run(() => LoadLyricsAsync(current, _generation, _playCts.Token));
            if (IsNeteaseCatalog && Section is "收藏" or "歌单" or "每日推荐" or "最近播放") Run(() => BrowseAsync(Section));
        }
        if (_dispatcher.CheckAccess()) Apply(); else _dispatcher.BeginInvoke(Apply);
    }
    public void SetSleep(int minutes, bool finishCurrent)
    {
        if (minutes is < 0 or > 720) throw new ApiException("请输入 0–720 分钟，0 表示播完当前歌曲停止。");
        if (minutes == 0 && (MobileOutput ? Current == null : Player.IsIdle)) throw new ApiException("请先播放一首歌曲。");
        _finishCurrent = finishCurrent; _stopAfterCurrent = minutes == 0; _sleepDeadline = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
        if (_stopAfterCurrent) CancelPrepared(); _sleepTimer.Start(); Changed(nameof(SleepStatus));
    }
    public void CancelSleep() { _sleepDeadline = null; _stopAfterCurrent = false; _sleepTimer.Stop(); Changed(nameof(SleepStatus)); PrepareNext(); }
    private void SleepTick(object? sender, EventArgs e)
    {
        if (_sleepDeadline is DateTime deadline && DateTime.Now >= deadline)
        {
            _sleepDeadline = null;
            if (_finishCurrent && (MobileOutput ? Current != null : !Player.IsIdle)) { _stopAfterCurrent = true; CancelPrepared(); }
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
        var track = Queue[next]; if (track.IsNetease) { CancelPrepared(); return; } string? url = track.IsLocal ? File.Exists(track.LocalPath) ? track.LocalPath : null : Offline.GetPath(track) ?? (Api.Configured ? Api.Url("stream", ("id", track.Id)) : null);
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
