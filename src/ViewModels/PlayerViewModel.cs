using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace ChiliMusic;

public sealed class RelayCommand(Action action) : ICommand
{
    public bool CanExecute(object? p) => true; public void Execute(object? p) => action(); public event EventHandler? CanExecuteChanged { add { } remove { } }
}
public sealed partial class PlayerViewModel : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed([CallerMemberName] string? name = null) { PropertyChanged?.Invoke(this, new(name)); if (name == nameof(PlayGlyph)) { PropertyChanged?.Invoke(this, new(nameof(PlayIcon))); Changed(nameof(IsProgressMoving)); } if (name == nameof(Position)) PropertyChanged?.Invoke(this, new(nameof(PositionText))); if (name == nameof(Duration)) PropertyChanged?.Invoke(this, new(nameof(DurationText))); }
    public AppSettings Settings { get; }
    public MusicApiClient Api { get; }
    public string CatalogSource { get => Settings.CatalogSource; set { value = value == "netease" ? "netease" : "navidrome"; if (value == Settings.CatalogSource) return; Settings.CatalogSource = value; SaveSettings(); Changed(); Changed(nameof(IsNeteaseCatalog)); Changed(nameof(ServerConfigured)); Changed(nameof(CatalogName)); _beforeSearch = null; Run(() => BrowseAsync("最近添加")); } }
    public bool IsNeteaseCatalog => Api.UsingNetease;
    public string CatalogName => Api.SourceName;
    public bool ServerConfigured => Api.Configured;
    public MpvPlayerService Player { get; }
    public CoverCacheService Covers { get; }
    public MediaSessionService? Media { get; set; }
    public PlaybackQueue Queue { get; } = [];
    public ObservableCollection<Track> Results { get; } = [];
    public ObservableCollection<LibraryItem> Library { get; } = [];
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public string Title => Current?.Title ?? "尚未播放";
    public string Artist => Current?.Artist ?? "选择音乐";
    public string Album => Current?.Album ?? "";
    public string FavoriteGlyph => Current?.Starred != null ? "♥" : "♡";
    private ImageSource? _cover;
    public ImageSource? Cover { get => _cover; set { _cover = value; Theme.SetCoverAccent(value as System.Windows.Media.Imaging.BitmapSource, Settings.CoverAccent); Changed(); } }
    private string _status = "等待连接";
    public string Status { get => _status; set { _status = value; Changed(); Changed(nameof(HasStatusError)); } }
    public bool HasStatusError => Status.Contains("失败") || Status.Contains("中断") || Status.Contains("重连") || Status.Contains("超时") || Status.Contains("不可") || Status.Contains("异常") || Status.Contains("不兼容") || Status.Contains("无法") || Status.Contains("找不到") || Status.Contains("请先") || Status.Contains("没有可播放");
    private string _section = "最近添加";
    public string Section { get => _section; set { _section = value; Changed(); } }
    private readonly List<Track> _localTracks = [];
    private string _navigation = "最近添加";
    public string Navigation { get => _navigation; private set { _navigation = value; Changed(); } }
    private string _quality = "尚未播放";
    public string Quality { get => _quality; set { _quality = value; Changed(); Changed(nameof(QualityTag)); } }
    public string QualityTag => Quality == "网易云试听" ? "试听" : Quality == "网易云播放" ? "网易云" : Quality.StartsWith("Direct Play") ? "Direct Play" : Quality == "服务端转码" ? "转码播放" : Quality == "手机播放" ? "手机播放" : Quality == "本地播放" ? "本地播放" : Quality == "播放中" ? "播放中" : "未播放";
    private string _audioInfo = "";
    public string AudioInfo { get => _audioInfo; set { _audioInfo = value; Changed(); Changed(nameof(AudioFields)); } }
    public IReadOnlyList<AudioField> AudioFields => AudioInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { int split = line.IndexOf('：'); return new AudioField(split > 0 ? line[..split] : line, split > 0 ? line[(split + 1)..] : ""); }).ToArray();
    private double _position;
    public double Position { get => _position; private set { if (Math.Abs(_position - value) < .01) return; bool clockChanged = (int)_position != (int)value; _position = value; PropertyChanged?.Invoke(this, new(nameof(Position))); if (clockChanged) { Changed(nameof(PositionText)); Changed(nameof(Time)); } } }
    public double Duration { get; private set; }
    public string Time => $"{Clock(Position)} / {Clock(Duration)}";
    public string PositionText => Clock(Position);
    public string DurationText => Clock(Duration);
    private static string Clock(double n) => TimeSpan.FromSeconds(Math.Max(0, n)).ToString(n >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string PlayGlyph => MobileOutput ? MobilePlaying ? "Ⅱ" : "▶" : Player.IsIdle || Player.IsPaused ? "▶" : "Ⅱ";
    public string PlayIcon => PlayGlyph == "▶" ? "\uE768" : "\uE769";
    public bool IsProgressMoving => MobileOutput ? MobilePlaying : _loaded && !Player.IsIdle && !Player.IsPaused && Player.Get("paused-for-cache") != "yes";
    private bool _progressMoving;
    public string ModeIcon => Settings.Mode switch { PlayMode.Shuffle => "\uE8B1", PlayMode.RepeatOne => "\uE8ED", PlayMode.RepeatAll => "\uE8EE", _ => "\uE72A" };
    public string ModeText => Settings.Mode switch { PlayMode.RepeatAll => "列表循环", PlayMode.RepeatOne => "单曲循环", PlayMode.Shuffle => "随机播放", _ => "顺序播放" };
    public double Volume { get => Settings.Volume; set { Settings.Volume = Math.Clamp(value, 0, 100); Player.Volume = Settings.Volume; Changed(); SaveSettingsSoon(); } }
    public string MuteText => Settings.Mute ? "静音" : "音量";
    public string Password { get; private set; }
    public ICommand ToggleCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand ModeCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand ShuffleCommand { get; }
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly DispatcherTimer _timer = new(); private readonly DispatcherTimer _saveTimer = new();
    private CancellationTokenSource _playCts = new(), _browseCts = new();
    private int _generation, _browseGeneration;
    private double _listened; private long _lastTick;
    private bool _submitted, _loaded, _disposed, _reconnecting, _recoveryLoad;
    private string? _responseType;
    private string? _coverPath;
    private double _restorePosition;
    public PlayerViewModel(AppSettings settings, string password)
    {
        Settings = settings; Password = password; Api = new(settings, password); Player = new(settings); Covers = new(Api);
        LocalLibrary = new(settings); Lyrics = new(Api); Offline = new(Api, settings); Playlists = new();
        Api.Netease.AccountChanged += OnNeteaseAccountChanged;
        Offline.CacheExtrasAsync = async (track, ct) => { await Covers.GetPathAsync(track.CoverArt, Settings.CoverCacheMb, ct); await Lyrics.LoadAsync(track, ct); };
        var saved = Store.Read("queue.json", new QueueState()); foreach (var track in saved.Tracks) Queue.Add(track);
        Index = saved.Index >= 0 && saved.Index < Queue.Count ? saved.Index : -1; _restorePosition = saved.Position;
        _order.Restore(Queue.Count, saved); if (_order.Cursor < 0 && Index >= 0) _order.Record(Index);
        Queue.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) { _order.Reset(Queue.Count); Index = -1; CancelPrepared(); return; }
            int Map(int index) => e.Action switch
            {
                System.Collections.Specialized.NotifyCollectionChangedAction.Add => index >= e.NewStartingIndex ? index + e.NewItems!.Count : index,
                System.Collections.Specialized.NotifyCollectionChangedAction.Remove => index >= e.OldStartingIndex && index < e.OldStartingIndex + e.OldItems!.Count ? -1 : index >= e.OldStartingIndex ? index - e.OldItems!.Count : index,
                System.Collections.Specialized.NotifyCollectionChangedAction.Move => index == e.OldStartingIndex ? e.NewStartingIndex : e.OldStartingIndex < e.NewStartingIndex && index > e.OldStartingIndex && index <= e.NewStartingIndex ? index - 1 : e.NewStartingIndex < e.OldStartingIndex && index >= e.NewStartingIndex && index < e.OldStartingIndex ? index + 1 : index,
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset => -1,
                _ => index
            };
            _order.Remap(Map, Queue.Count, e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add ? Enumerable.Range(e.NewStartingIndex, e.NewItems!.Count) : []);
            Index = Map(Index); CancelPrepared(); if (Current != null && _loaded) PrepareNext();
        };
        LocalLibrary.Updated += OnLocalLibraryUpdated; LocalLibrary.Progress += OnLibraryProgress;
        Offline.Progress += message => _dispatcher.BeginInvoke(() => { if (!_disposed) DownloadStatus = message; });
        Offline.Updated += () => _dispatcher.BeginInvoke(() => { if (_disposed) return; Changed(nameof(OfflineEntries)); Changed(nameof(OfflineSize)); Changed(nameof(IsDownloading)); if (Navigation == "离线音乐") Run(() => BrowseAsync("离线音乐")); });
        Run(LocalLibrary.InitializeAsync);
        ToggleCommand = new RelayCommand(() => Run(ToggleAsync)); NextCommand = new RelayCommand(() => Run(() => NextAsync(false))); PreviousCommand = new RelayCommand(() => Run(PreviousAsync));
        FavoriteCommand = new RelayCommand(() => Run(ToggleFavoriteAsync)); ModeCommand = new RelayCommand(CycleMode); MuteCommand = new RelayCommand(ToggleMute); ShuffleCommand = new RelayCommand(() => { Settings.Mode = Settings.Mode == PlayMode.Shuffle ? PlayMode.Sequential : PlayMode.Shuffle; Changed(nameof(ModeText)); Changed(nameof(ModeIcon)); SaveSettings(); PrepareNext(); });
        _timer.Interval = TimeSpan.FromMilliseconds(500); _timer.Tick += Tick;
        _saveTimer.Interval = TimeSpan.FromSeconds(1); _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };
        Player.Loaded += () => { int generation = _generation; _dispatcher.BeginInvoke(() => { if (!_disposed && generation == _generation) Run(OnLoadedAsync); }); };
        Player.Ended += code => { int generation = _generation; _dispatcher.BeginInvoke(() => { if (_disposed || generation != _generation) return; if (code == 0) { if (_stopAfterCurrent) { CancelSleep(); Stop(); } else if (_preparedIndex < 0) Run(() => NextAsync(true)); } else Run(RecoverDeviceOrNetworkAsync); }); };
        _sleepTimer.Interval = TimeSpan.FromSeconds(1); _sleepTimer.Tick += SleepTick;
    }
    public async Task ConnectAsync() { Status = $"正在连接 {Api.SourceName}…"; var version = await Api.PingAsync(); Status = $"已连接 · {version}"; Store.Log("INFO", $"{Api.SourceName} 连接成功"); }
    public void Run(Func<Task> action) => _ = Guard(action);
    private async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Status = e is ApiException ? e.Message : "操作失败，请重试或查看日志。"; Store.Log("ERROR", $"操作失败 {e.GetType().Name}"); }
    }
    public void UpdateSettings(AppSettings updated, string password)
    {
        bool active = !Player.IsIdle, paused = Player.IsPaused; double position = Player.Position;
        Player.Stop(); _loaded = false; _timer.Stop(); _playCts.Cancel(); Offline.Cancel(); Offline.ProtectedPath = null;
        bool otherServer = !string.Equals(Settings.Server, updated.Server, StringComparison.OrdinalIgnoreCase) || Settings.Username != updated.Username;
        foreach (var prop in typeof(AppSettings).GetProperties()) prop.SetValue(Settings, prop.GetValue(updated));
        Password = password; Api.Configure(Settings, password);
        Player.Set("tls-verify", Settings.AllowUntrustedCertificate ? "no" : "yes"); Player.Set("audio-exclusive", Settings.Exclusive ? "yes" : "no");
        Player.Set("audio-device", Settings.AudioDevice); Player.Set("replaygain", Settings.ReplayGain); Player.Set("replaygain-preamp", Settings.ReplayGainPreamp.ToString(System.Globalization.CultureInfo.InvariantCulture)); Player.Set("prefetch-playlist", Settings.PrefetchNext ? "yes" : "no");
        Player.Volume = Settings.Volume; Player.Set("mute", Settings.Mute ? "yes" : "no");
        if (otherServer) { Queue.Clear(); Index = -1; Results.Clear(); Library.Clear(); Cover = null; SaveQueue(); RefreshTrack(); }
        CancelPrepared(); Changed(nameof(Volume)); Changed(nameof(MuteText)); Changed(nameof(PlayGlyph)); Changed(nameof(ServerConfigured)); SaveSettings(); Theme.Apply(Settings.Theme); Theme.SetCoverAccent(Cover as System.Windows.Media.Imaging.BitmapSource, Settings.CoverAccent); Run(Offline.EnforceLimitAsync); SettingsChanged?.Invoke();
        if (!otherServer && active && Current != null) { _restorePosition = position; Run(async () => { await PlayAsync(Index, true); if (paused) { Player.Pause(); Changed(nameof(PlayGlyph)); } }); }
    }
    private void SaveSettingsSoon() { _saveTimer.Stop(); _saveTimer.Start(); }
    public void SaveSettings() => Store.Write("config.json", Settings);
    public void SaveQueue() => Store.Write("queue.json", new QueueState { Tracks = Queue.ToList(), Index = Index, Position = Position, History = _order.History.ToList(), HistoryPosition = _order.Cursor, ShuffleRemaining = _order.Remaining.ToList() });
    private void RefreshTrack() { Changed(nameof(Current)); Changed(nameof(Title)); Changed(nameof(Artist)); Changed(nameof(Album)); Changed(nameof(FavoriteGlyph)); Changed(nameof(PlayGlyph)); }
    public async Task PlayAsync(int index, bool resume = false)
    {
        if (index < 0 || index >= Queue.Count) return;
        CancelPrepared(); _order.Record(index);
        _playCts.Cancel(); _playCts.Dispose(); _playCts = new(); var ct = _playCts.Token; var generation = ++_generation;
        Player.Stop(); _timer.Stop(); _loaded = false; _reconnecting = false; _recoveryLoad = false; Index = index; Position = 0; Duration = Current!.Duration; Changed(nameof(Duration));
        _listened = 0; _submitted = false; _mobileHistoryStarted = false; Cover = null; _coverPath = null; Quality = "正在加载"; AudioInfo = ""; _responseType = null; ClearLyrics(); RefreshTrack(); SaveQueue();
        var track = Current; Status = $"正在加载 · {track.Title}";
        Offline.ProtectedPath = null;
        if (MobileOutput) { SeekRevision++; Position = resume ? _restorePosition : 0; _restorePosition = 0; MobilePlaying = true; Quality = "手机播放"; Status = "正在手机播放"; RefreshTrack(); SaveQueue(); _ = LoadExtrasAsync(track, generation, ct); return; }
        if (track.IsLocal) { if (!File.Exists(track.LocalPath)) throw new ApiException("找不到音乐文件，请重新打开。"); Player.Load(track.LocalPath!, resume ? _restorePosition : 0); }
        else { string? cached = Offline.GetPath(track); if (cached == null && !track.IsNetease && !Api.NavidromeConfigured) throw new ApiException("请先在设置中连接 Navidrome。"); Offline.ProtectedPath = cached; var url = cached ?? await Api.StreamUrlAsync(track, ct); if (generation != _generation || ct.IsCancellationRequested) return; Player.Load(url, resume ? _restorePosition : 0); }
        _restorePosition = 0;
        _ = LoadExtrasAsync(track, generation, ct);
        await Task.CompletedTask;
    }
    private async Task LoadExtrasAsync(Track track, int generation, CancellationToken ct)
    {
        try
        {
            var path = track.IsLocal ? track.EmbeddedCoverPath ?? new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }.Select(name => Path.Combine(Path.GetDirectoryName(track.LocalPath!)!, name)).FirstOrDefault(File.Exists) : await Covers.GetPathAsync(track.CoverArt, Settings.CoverCacheMb, ct);
            var image = await Task.Run(() => CoverCacheService.Load(path), ct);
            if (generation != _generation || ct.IsCancellationRequested) return; _coverPath = path; Cover = image;
            if (Media != null) await Media.UpdateAsync(track, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Store.Log("WARNING", $"封面或媒体信息更新失败 {e.GetType().Name}"); }
        Run(() => LoadLyricsAsync(track, generation, ct));
        if (track.IsLocal || track.IsNetease || Offline.GetPath(track) != null) return;
        try { var type = await Api.ProbeTypeAsync(track.Id, ct); if (generation == _generation && !ct.IsCancellationRequested) { _responseType = type; UpdateAudioInfo(); } }
        catch (OperationCanceledException) { }
        catch (Exception e) { Store.Log("WARNING", $"响应格式检查失败 {e.GetType().Name}"); }
    }
    private async Task OnLoadedAsync()
    {
        if (_disposed || MobileOutput) return;
        if (_preparedIndex >= 0 && Player.Get("playlist-pos") == "1")
        {
            int next = _preparedIndex; _preparedIndex = -1; _playCts.Cancel(); _playCts.Dispose(); _playCts = new(); _generation++; Index = next; _order.Next(_order.History.Count > 0 ? _order.History[_order.Cursor] : -1, Settings.Mode, true); _order.Record(next);
            Offline.ProtectedPath = Current?.IsLocal == true ? null : Current == null ? null : Offline.GetPath(Current);
            Position = 0; _listened = 0; _submitted = false; _responseType = null; Cover = null; _coverPath = null; ClearLyrics(); RefreshTrack(); SaveQueue(); _ = LoadExtrasAsync(Current!, _generation, _playCts.Token);
        }
        if (_disposed || Current == null) return; _loaded = true; _reconnecting = false; _lastTick = Stopwatch.GetTimestamp(); _timer.Start();
        if (Current.IsLocal) { foreach (var field in new[] { "title", "artist", "album" }) { var value = Player.Get("metadata/by-key/" + field); if (!string.IsNullOrWhiteSpace(value)) { if (field == "title") Current.Title = value; else if (field == "artist") Current.Artist = value; else Current.Album = value; } } Current.NotifyMetadata(); RefreshTrack(); SaveLocalLibrary(); }
        Status = Current.Preview ? "正在试听 · 完整播放需要相应权益" : "正在播放"; if (Current.IsNetease) Api.Netease.RecordRecent(Current); Duration = Player.Duration > 0 ? Player.Duration : Current.Duration; if (Current.IsLocal) { Current.Duration = Duration; Current.NotifyMetadata(); SaveLocalLibrary(); }
        Changed(nameof(Duration)); Changed(nameof(PlayGlyph)); UpdateAudioInfo();
        PrepareNext();
        Media?.Status(false, false);
        if (!_recoveryLoad) { var playing = Current; if (Media != null) await Media.UpdateAsync(playing, _coverPath); if (!playing.IsLocal) Run(() => ScrobbleQuietlyAsync(playing.Id, false)); ((App)Application.Current).NotifyTrack(playing); }
        _recoveryLoad = false;
    }
    private static string Normalize(string v) => v.ToLowerInvariant() switch { "ape" or "monkey's audio" => "ape", "aac" or "m4a" => "aac", "alac" => "alac", "mp3" or "mp2" => "mp3", "pcm_s16le" or "pcm_s24le" or "pcm_f32le" or "wav" => "pcm", "vorbis" or "ogg" => "vorbis", _ => v.ToLowerInvariant() };
    private void UpdateAudioInfo()
    {
        if (Current == null) return; if (MobileOutput) { Quality = "手机播放"; return; } var codec = Player.Get("audio-codec-name"); var expected = Normalize(Current.Suffix ?? ""); var actual = Normalize(codec);
        bool containerAmbiguous = expected is "aac" or "vorbis" or "dsf" or "dff";
        bool mismatch = codec.Length > 0 && !containerAmbiguous && expected.Length > 0 && actual != expected;
        bool httpMismatch = _responseType != null && Current.ContentType != null && !string.Equals(_responseType, Current.ContentType, StringComparison.OrdinalIgnoreCase) && !(_responseType == "application/octet-stream");
        Quality = Current.IsNetease ? Current.Preview ? "网易云试听" : "网易云播放" : Current.IsLocal ? "本地播放" : mismatch || httpMismatch ? "服务端转码" : codec.Length > 0 && expected.Length > 0 && !containerAmbiguous && actual == expected ? "Direct Play" : "播放中";
        var rate = Player.Get("audio-params/samplerate"); var sampleFormat = Player.Get("audio-params/format"); var channels = Player.Get("audio-params/channel-count");
        static string Value(int n, string unit) => n > 0 ? $"{n} {unit}" : "—";
        AudioInfo = $"格式：{Current.Format}\n采样率：{(Current.SamplingRate > 0 ? Value(Current.SamplingRate, "Hz") : rate.Length > 0 ? rate + " Hz" : "—")}\n位深：{Value(Current.BitDepth, "bit")}\n声道：{(Current.ChannelCount > 0 ? Current.ChannelCount.ToString() : channels.Length > 0 ? channels : "—")}\n码率：{Value(Current.BitRate, "kbps")}\n播放：{Quality}\n解码：{(codec.Length > 0 ? codec.ToUpperInvariant() : "—")}\n输出：WASAPI{(Settings.Exclusive ? " 独占" : " 共享")}";
        if (!Current.IsLocal && (mismatch || httpMismatch)) Store.Log("WARNING", "服务器返回的 Content-Type / 编码与源文件不同，可能发生服务器端转码。");
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (!_loaded || Current == null) return;
        var now = Stopwatch.GetTimestamp(); double elapsed = (now - _lastTick) / (double)Stopwatch.Frequency; _lastTick = now;
        Position = Player.Position; double duration = Player.Duration > 0 ? Player.Duration : Current.Duration; if (Math.Abs(Duration - duration) > .01) { Duration = duration; Changed(nameof(Duration)); }
        UpdateLyricPosition();
        bool paused = Player.IsPaused; bool idle = Player.IsIdle; Media?.Status(idle, paused);
        if (!paused && !idle && Player.Get("paused-for-cache") != "yes") _listened += Math.Min(elapsed, 2);
        if (!Current.IsLocal && !_submitted && Duration > 0 && _listened >= Math.Min(Duration / 2, 240)) { _submitted = true; var id = Current.Id; Run(() => ScrobbleQuietlyAsync(id, true)); }
        bool moving = !paused && !idle && Player.Get("paused-for-cache") != "yes";
        if (moving != _progressMoving) { _progressMoving = moving; Changed(nameof(IsProgressMoving)); }
        _timer.Interval = TimeSpan.FromMilliseconds(paused ? 500 : Application.Current.Windows.Cast<Window>().Any(w => w.IsVisible) ? 50 : 250);
    }
    public async Task ToggleAsync()
    {
        if (MobileOutput) { if (Current == null) { if (Queue.Count > 0) await PlayAsync(0); else await RandomAsync(false); } else { MobilePlaying = !MobilePlaying; Changed(nameof(PlayGlyph)); } return; }
        if (Player.IsIdle) { if (Current != null) await PlayAsync(Index, true); else if (Queue.Count > 0) await PlayAsync(0); else await RandomAsync(false); }
        else if (Player.IsPaused) Player.Resume(); else Player.Pause(); Changed(nameof(PlayGlyph)); Media?.Status(Player.IsIdle, Player.IsPaused);
    }
    public void Stop() { if (MobileOutput) SeekRevision++; MobilePlaying = false; CancelPrepared(); _playCts.Cancel(); _loaded = false; _reconnecting = false; _timer.Stop(); Player.Stop(); Position = 0; _restorePosition = 0; Offline.ProtectedPath = null; Changed(nameof(PlayGlyph)); Media?.Status(true, false); Status = "已停止"; SaveQueue(); }
    public void Seek(double position) { SeekRevision++; if (MobileOutput) { Position = Math.Clamp(position, 0, Duration); UpdateLyricPosition(); SaveQueue(); } else if (!Player.IsIdle) { Player.Seek(position); Position = position; UpdateLyricPosition(); SaveQueue(); } }
    public async Task NextAsync(bool automatic)
    {
        if (automatic && _stopAfterCurrent) { CancelSleep(); Stop(); return; }
        if (Queue.Count == 0) { Stop(); return; }
        int next = _order.Next(Index, Settings.Mode, automatic);
        if (next < 0) { Stop(); Status = "队列播放完毕"; return; }
        await PlayAsync(next);
    }
    public Task PreviousAsync() { int previous = _order.Previous(); return PlayAsync(previous >= 0 ? previous : Math.Max(0, Index - 1)); }
    public void CycleMode() { Settings.Mode = (PlayMode)(((int)Settings.Mode + 1) % 4); Changed(nameof(ModeText)); Changed(nameof(ModeIcon)); SaveSettings(); PrepareNext(); }
    public void ToggleMute() { Settings.Mute = !Settings.Mute; Player.Set("mute", Settings.Mute ? "yes" : "no"); Changed(nameof(MuteText)); SaveSettings(); }
    public async Task ToggleFavoriteAsync()
    {
        var track = Current; if (track == null) return; bool star = track.Starred == null; if (!track.IsLocal) await Api.StarAsync(track.Id, star);
        foreach (var t in Queue.Concat(Results).Concat(_localTracks).Where(t => t.Id == track.Id)) { t.Starred = star ? DateTime.UtcNow.ToString("O") : null; t.NotifyMetadata(); }
        Changed(nameof(FavoriteGlyph)); SaveQueue(); if (track.IsLocal) SaveLocalLibrary(); Status = star ? "已收藏" : "已取消收藏";
    }
    public async Task RandomAsync(bool favorites)
    {
        Status = "正在获取随机歌曲…"; var tracks = Navigation == "本地音乐" || !Api.Configured ? _localTracks.Where(t => !favorites || t.Starred != null).ToList() : favorites ? await Api.StarredAsync() : await Api.RandomAsync();
        if (favorites || Navigation == "本地音乐" || !Api.Configured) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tracks));
        if (tracks.Count == 0) { Status = favorites ? "还没有收藏歌曲" : "音乐库中没有歌曲"; return; }
        ReplaceQueue(tracks); await PlayAsync(0);
    }
    public async Task PlayTrackAsync(Track track) { Queue.Add(track); await PlayAsync(Queue.Count - 1); }
    public void ReplaceQueue(IEnumerable<Track> tracks) { Stop(); Queue.ReplaceWith(tracks); RefreshTrack(); SaveQueue(); }
    public void Add(Track track, bool next) { if (next && Index >= 0) Queue.Insert(Index + 1, track); else Queue.Add(track); SaveQueue(); Status = next ? "已加入下一首" : "已加入队列"; }
    public void Move(int from, int to) { if (from < 0 || to < 0 || from >= Queue.Count || to >= Queue.Count) return; var current = Current; Queue.Move(from, to); Index = current == null ? -1 : Queue.IndexOf(current); SaveQueue(); RefreshTrack(); }
    public void Remove(int index)
    {
        if (index < 0 || index >= Queue.Count) return; bool current = index == Index; var active = Current;
        if (current) Stop(); Queue.RemoveAt(index); Index = current ? -1 : active == null ? -1 : Queue.IndexOf(active); RefreshTrack(); SaveQueue();
    }
    public void ClearQueue() { Stop(); Queue.Clear(); Index = -1; Cover = null; Quality = "尚未播放"; RefreshTrack(); SaveQueue(); }
    private async Task ReconnectAsync()
    {
        if (Current?.IsLocal == true) { Stop(); Status = "无法播放这个文件，请检查文件是否完整。"; return; }
        if (_reconnecting || Current == null || _disposed) return; _reconnecting = true; _loaded = false; _timer.Stop(); var ct = _playCts.Token; int generation = _generation; double start = Position; bool paused = Player.IsPaused;
        int[] delays = [1, 2, 5, 10, 30]; int attempt = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int delay = delays[Math.Min(attempt++, delays.Length - 1)]; Status = $"网络或音频流中断 · {delay} 秒后重连"; await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                try { var recoveredUrl = await Api.StreamUrlAsync(Current, ct); if (generation != _generation) return; _recoveryLoad = true; Player.Load(recoveredUrl, start); if (paused) Player.Pause(); Status = "正在重新加载音频流…"; return; }
                catch (ApiException) { Store.Log("WARNING", "网络重连失败"); }
            }
        }
        finally { _reconnecting = false; }
    }
    public static readonly HashSet<string> LocalExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".ape", ".aac", ".m4a", ".alac", ".wav", ".ogg", ".opus", ".wma", ".dsf", ".dff", ".aif", ".aiff", ".wv", ".tta", ".tak", ".mpc", ".ac3", ".dts", ".cue" };
    private void SaveLocalLibrary() => Run(LocalLibrary.PersistAsync);
    public async Task OpenLocalFilesAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths) { if (Directory.Exists(path)) await LocalLibrary.AddFolderAsync(path); else if (Path.GetExtension(path).Equals(".m3u8", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase)) files.AddRange(await PlaylistService.ImportPathsAsync(path)); else files.Add(path); }
        await LocalLibrary.ImportAsync(files); var full = files.Where(File.Exists).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase); var added = _localTracks.Where(t => t.LocalPath != null && full.Contains(t.LocalPath)).ToList();
        await BrowseAsync("本地音乐"); if (added.Count == 0) return; int first = Queue.Count; foreach (var track in added) Queue.Add(track); await PlayAsync(first);
    }
    private async Task ScrobbleQuietlyAsync(string id, bool submission) { try { await Api.ScrobbleAsync(id, submission); } catch (ApiException) { Store.Log("WARNING", "播放记录暂时无法同步"); } }
    public void Dispose() { if (_disposed) return; SaveQueue(); SaveSettings(); _disposed = true; LocalLibrary.Dispose(); Offline.Dispose(); _sleepTimer.Stop(); _playCts.Cancel(); _browseCts.Cancel(); _timer.Stop(); _saveTimer.Stop(); Media?.Dispose(); Player.Dispose(); Api.Dispose(); _playCts.Dispose(); _browseCts.Dispose(); }
}
