using System.Text.Json;
using System.Windows.Data;

namespace ChiliMusic;

public sealed partial class PlayerViewModel
{
    public LibraryItem? BrowseItem => _browseItem;
    public bool IsLocalCatalog => CatalogSource == "local";
    public string LocalView { get => Settings.LocalView; set { if (value == Settings.LocalView) return; Settings.LocalView = value; Changed(); SaveSettings(); if (IsLocalCatalog || Navigation == "本地音乐") Run(() => BrowseAsync("本地音乐")); } }
    public string SearchScope { get => Settings.SearchScope; set { Settings.SearchScope = value; SaveSettings(); Changed(); } }
    public List<string> SearchHistory { get; } = Store.Read("search-history.json", new List<string>());
    public List<ListeningEntry> ListeningHistory { get; } = Store.Read("listening-history.json", new List<ListeningEntry>());
    private readonly Dictionary<string, List<Track>> _listeningContexts = Store.Read("listening-contexts.json", new Dictionary<string, List<Track>>());
    public event Action? PlaylistsChanged;
    public void NotifyPlaylists() { PlaylistsChanged?.Invoke(); if (Section == "歌单") Run(() => BrowseAsync("歌单")); }
    public string BrowseSummary => $"已加载 {Results.Count} 首 / {Library.Count} 个项目{(HasMore ? " · 还有更多" : " · 已全部加载")}";
    public string QueueSummary => $"{Queue.Count} 首 · {DurationLabel(Queue.Sum(t => Math.Max(0, t.Duration)))}";
    public string PrioritySummary => _order.Priority.Count > 0 ? $"安排下一首：{string.Join(" → ", _order.Priority.Where(i => i >= 0 && i < Queue.Count).Take(5).Select(i => Queue[i].Title))}" : "按当前播放模式继续";
    public string AlbumDetail => _browseItem == null ? "" : Results.Count == 0 && Library.Count > 0 ? $"{Library.Count} 张专辑" : string.Join(" · ", new[] { Results.FirstOrDefault()?.Year is > 0 ? Results[0].Year.ToString() : "", Results.FirstOrDefault()?.Genre ?? "", $"{Results.Count} 首", DurationLabel(Results.Sum(t => t.Duration)) }.Where(s => s.Length > 0));
    private static string DurationLabel(double seconds) { var time = TimeSpan.FromSeconds(Math.Max(0, seconds)); return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}"; }
    public string PlaybackFailure { get; private set; } = "";
    private int _recoveryAttempts;
    public void ClearPlaybackFailure() { PlaybackFailure = ""; Changed(nameof(PlaybackFailure)); }
    public async Task RetryPlaybackAsync() { _recoveryAttempts = 0; ClearPlaybackFailure(); _restorePosition = Position; if (Index >= 0) await PlayAsync(Index, true); }
    private static bool PermanentFailure(Exception error) => error.Message.Contains("认证") || error.Message.Contains("权限") || error.Message.Contains("版权") || error.Message.Contains("权益") || error.Message.Contains("找不到") || error.Message.Contains("不兼容") || error.Message.Contains("证书");
    private void SetPlaybackFailure(string text) { PlaybackFailure = text; Status = text; Changed(nameof(PlaybackFailure)); Changed(nameof(PlayGlyph)); }
    private void RememberSearch(string query)
    {
        if (query.Trim().Length == 0) return; SearchHistory.RemoveAll(s => s.Equals(query.Trim(), StringComparison.CurrentCultureIgnoreCase)); SearchHistory.Insert(0, query.Trim()); if (SearchHistory.Count > 20) SearchHistory.RemoveRange(20, SearchHistory.Count - 20); Store.Write("search-history.json", SearchHistory);
    }
    public void ClearSearchHistory() { SearchHistory.Clear(); Store.Write("search-history.json", SearchHistory); }
    private void RecordListening()
    {
        if (Current == null || _historyGeneration == _generation) return; _historyGeneration = _generation;
        string contextId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Api.ScopeForId(Current.Id) + string.Join("\0", Queue.Select(t => t.Id)))));
        if (!_listeningContexts.ContainsKey(contextId)) { _listeningContexts[contextId] = Queue.ToList(); while (_listeningContexts.Count > 50) _listeningContexts.Remove(_listeningContexts.Keys.First()); Store.Write("listening-contexts.json", _listeningContexts); }
        ListeningHistory.Insert(0, new() { Track = Current, PlayedUtc = DateTime.UtcNow, Scope = Api.ScopeForId(Current.Id), ContextId = contextId });
        if (ListeningHistory.Count > 300) ListeningHistory.RemoveRange(300, ListeningHistory.Count - 300); Store.Write("listening-history.json", ListeningHistory);
    }
    private int _historyGeneration = -1;
    public void ClearListeningHistory() { ListeningHistory.Clear(); Store.Write("listening-history.json", ListeningHistory); _listeningContexts.Clear(); Store.Write("listening-contexts.json", _listeningContexts); if (Section == "本机历史") Run(() => BrowseAsync("本机历史")); }
    public async Task PlayHistoryAsync(ListeningEntry entry) { var context = _listeningContexts.GetValueOrDefault(entry.ContextId) ?? entry.Context; await PlayFromListAsync(entry.Track, context.Any(t => t.Id == entry.Track.Id) ? context : [entry.Track]); }
    private readonly Stack<(QueueState State, bool Playing)> _queueUndo = new();
    private bool _undoing;
    private void RememberQueue()
    {
        if (_undoing) return;
        _queueUndo.Push((new QueueState { Tracks = Queue.ToList(), Index = Index, Position = Position, History = _order.History.ToList(), HistoryPosition = _order.Cursor, ShuffleRemaining = _order.Remaining.ToList(), PlayNext = _order.Priority.ToList() }, MobileOutput ? MobilePlaying : !Player.IsPaused && !Player.IsIdle));
        if (_queueUndo.Count > 20) { var keep = _queueUndo.Take(20).Reverse().ToArray(); _queueUndo.Clear(); foreach (var state in keep) _queueUndo.Push(state); } Changed(nameof(CanUndoQueue));
    }
    public bool CanUndoQueue => _queueUndo.Count > 0;
    public async Task UndoQueueAsync()
    {
        if (!_queueUndo.TryPop(out var snapshot)) return; _undoing = true;
        try { Stop(); Queue.ReplaceWith(snapshot.State.Tracks); Index = snapshot.State.Index; _order.Restore(Queue.Count, snapshot.State); _restorePosition = snapshot.State.Position; RefreshTrack(); if (Index >= 0) { await PlayAsync(Index, true); if (!snapshot.Playing) PausePlayback(); } SaveQueue(); }
        finally { _undoing = false; Changed(nameof(CanUndoQueue)); }
    }
    public void RemoveRange(IEnumerable<int> indices)
    {
        var selected = indices.Where(i => i >= 0 && i < Queue.Count).Distinct().OrderDescending().ToArray(); if (selected.Length == 0) return;
        RememberQueue(); bool removedCurrent = selected.Contains(Index), playing = MobileOutput ? MobilePlaying : !Player.IsIdle && !Player.IsPaused; int next = Index;
        if (removedCurrent) Stop(); foreach (int i in selected) Queue.RemoveAt(i);
        if (removedCurrent) { Index = Queue.Count == 0 ? -1 : Math.Clamp(next - selected.Count(i => i < next), 0, Queue.Count - 1); if (Index >= 0) Run(async () => { await PlayAsync(Index); if (!playing) PausePlayback(); }); }
        RefreshTrack(); SaveQueue();
    }
    public async Task PlayCompleteBrowseAsync(Track? selected = null)
    {
        var generation = _browseGeneration; var songs = Results.ToList();
        // Complete the active result set without changing its navigation or viewport.
        if (HasMore)
        {
            var state = CaptureBrowseState();
            while (HasMore && generation == _browseGeneration)
            {
                int before = Results.Count + Library.Count; await LoadMoreAsync();
                if (Results.Count + Library.Count == before) break;
            }
            if (generation != _browseGeneration) return;
            songs = Results.ToList(); BrowseArrived?.Invoke(state.Viewport);
        }
        songs = CollectionViewSource.GetDefaultView(Results).Cast<Track>().Where(MatchesTrackFilter).ToList();
        if (songs.Count == 0) { Status = "没有可播放的歌曲"; return; }
        await PlayFromListAsync(selected ?? songs[0], songs);
    }
    private bool MatchesTrackFilter(Track t) => (FormatFilter == "全部格式" || t.Format.Equals(FormatFilter, StringComparison.OrdinalIgnoreCase)) && Contains(t.Artist, ArtistFilter) && Contains(t.Album, AlbumFilter);
    private bool _completingFilter;
    private async Task CompleteFilterAsync()
    {
        if (_completingFilter || !HasMore || FormatFilter == "全部格式" && ArtistFilter.Length == 0 && AlbumFilter.Length == 0) return;
        _completingFilter = true; int generation = _browseGeneration;
        try { while (HasMore && generation == _browseGeneration) { int count = Results.Count + Library.Count; await LoadMoreAsync(); if (Results.Count + Library.Count == count) break; } }
        finally { _completingFilter = false; }
    }
    public string DetailedOutput
    {
        get
        {
            string sourceRate = Player.Get("audio-params/samplerate"), outputRate = Player.Get("audio-out-params/samplerate");
            string device = Settings.AudioDevice == "auto" ? "系统默认输出（由 Windows 选择）" : Player.AudioDevices().FirstOrDefault(d => d.Name == Settings.AudioDevice)?.Description ?? Settings.AudioDevice;
            string gain = Settings.ReplayGain == "album" ? Player.Get("track-list/0/replaygain-album-gain") : Player.Get("track-list/0/replaygain-track-gain");
            return $"设备：{device}\n输出驱动：{Player.Get("current-ao")}\n实际输出：{outputRate} Hz / {Player.Get("audio-out-params/format")} / {Player.Get("audio-out-params/channel-count")} 声道\n重采样：{(sourceRate.Length == 0 || outputRate.Length == 0 ? "尚无输出" : sourceRate == outputRate ? "采样率一致" : sourceRate + " → " + outputRate + " Hz")}\nReplayGain：{(Settings.ReplayGain == "no" ? "关闭" : gain.Length == 0 ? "未发现响度标签" : Settings.ReplayGain + " · " + gain + " dB")} · 预增益 {Settings.ReplayGainPreamp:0.#} dB\n均衡器：{(Settings.EqualizerEnabled ? "已启用" : "关闭")} · 防削波保护开启";
        }
    }
    public void ApplyAudioProcessing()
    {
        Player.Set("replaygain-clip", "no");
        var bands = new[] { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        var values = Settings.Equalizer.Length == 10 ? Settings.Equalizer : new double[10];
        string filters = Settings.EqualizerEnabled ? string.Join(",", bands.Select((hz, i) => $"lavfi=[equalizer=f={hz}:t=o:w=1:g={Math.Clamp(values[i], -12, 12).ToString(System.Globalization.CultureInfo.InvariantCulture)}]")) + ",lavfi=[alimiter=limit=0.95:level=false]" : "";
        Player.Set("af", filters); Changed(nameof(DetailedOutput));
    }
    public void ApplyLyricAppearance()
    {
        Application.Current.Resources["LyricFontSize"] = Math.Clamp(Settings.LyricFontSize, 14, 38);
        Application.Current.Resources["LyricMargin"] = new System.Windows.Thickness(12, Math.Clamp(Settings.LyricSpacing, 0, 24), 12, Math.Clamp(Settings.LyricSpacing, 0, 24));
        Application.Current.Resources["DesktopLyricOpacity"] = Math.Clamp(Settings.LyricBackgroundOpacity, .2, 1);
        Application.Current.Resources["DesktopLyricBrush"] = string.IsNullOrWhiteSpace(Settings.DesktopLyricColor) ? Application.Current.FindResource("TextBrush") : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.DesktopLyricColor));
    }
}
