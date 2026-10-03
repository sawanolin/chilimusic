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
public sealed class PlayerViewModel : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed([CallerMemberName] string? name = null) { PropertyChanged?.Invoke(this, new(name)); if (name == nameof(PlayGlyph)) PropertyChanged?.Invoke(this, new(nameof(PlayIcon))); if (name == nameof(Position)) PropertyChanged?.Invoke(this, new(nameof(PositionText))); if (name == nameof(Duration)) PropertyChanged?.Invoke(this, new(nameof(DurationText))); }
    public AppSettings Settings { get; }
    public NavidromeApiClient Api { get; }
    public bool ServerConfigured => Api.Configured;
    public MpvPlayerService Player { get; }
    public CoverCacheService Covers { get; }
    public MediaSessionService? Media { get; set; }
    public ObservableCollection<Track> Queue { get; } = [];
    public ObservableCollection<Track> Results { get; } = [];
    public ObservableCollection<LibraryItem> Library { get; } = [];
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public string Title => Current?.Title ?? "尚未播放";
    public string Artist => Current?.Artist ?? "选择音乐";
    public string Album => Current?.Album ?? "";
    public string FavoriteGlyph => Current?.Starred != null ? "♥" : "♡";
    private ImageSource? _cover;
    public ImageSource? Cover { get => _cover; set { _cover = value; Changed(); } }
    private string _status = "等待连接";
    public string Status { get => _status; set { _status = value; Changed(); Changed(nameof(HasStatusError)); } }
    public bool HasStatusError => Status.Contains("失败") || Status.Contains("中断") || Status.Contains("重连") || Status.Contains("超时") || Status.Contains("不可") || Status.Contains("异常") || Status.Contains("不兼容") || Status.Contains("无法") || Status.Contains("找不到") || Status.Contains("请先") || Status.Contains("没有可播放");
    private string _section = "最近添加";
    public string Section { get => _section; set { _section = value; Changed(); } }
    private readonly List<Track> _localTracks = Store.Read("local-library.json", new List<Track>());
    private string _navigation = "最近添加";
    public string Navigation { get => _navigation; private set { _navigation = value; Changed(); } }
    private string _quality = "尚未播放";
    public string Quality { get => _quality; set { _quality = value; Changed(); Changed(nameof(QualityTag)); } }
    public string QualityTag => Quality.StartsWith("Direct Play") ? "Direct Play" : Quality == "服务端转码" ? "转码播放" : Quality == "本地播放" ? "本地播放" : Quality == "播放中" ? "播放中" : "未播放";
    private string _audioInfo = "";
    public string AudioInfo { get => _audioInfo; set { _audioInfo = value; Changed(); Changed(nameof(AudioFields)); } }
    public IReadOnlyList<AudioField> AudioFields => AudioInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { int split = line.IndexOf('：'); return new AudioField(split > 0 ? line[..split] : line, split > 0 ? line[(split + 1)..] : ""); }).ToArray();
    private double _position;
    public double Position { get => _position; private set { _position = value; Changed(); Changed(nameof(Time)); } }
    public double Duration { get; private set; }
    public string Time => $"{Clock(Position)} / {Clock(Duration)}";
    public string PositionText => Clock(Position);
    public string DurationText => Clock(Duration);
    private static string Clock(double n) => TimeSpan.FromSeconds(Math.Max(0, n)).ToString(n >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string PlayGlyph => Player.IsIdle || Player.IsPaused ? "▶" : "Ⅱ";
    public string PlayIcon => Player.IsIdle || Player.IsPaused ? "\uE768" : "\uE769";
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
        var saved = Store.Read("queue.json", new QueueState()); foreach (var track in saved.Tracks) Queue.Add(track);
        Index = saved.Index >= 0 && saved.Index < Queue.Count ? saved.Index : -1; _restorePosition = saved.Position;
        ToggleCommand = new RelayCommand(() => Run(ToggleAsync)); NextCommand = new RelayCommand(() => Run(() => NextAsync(false))); PreviousCommand = new RelayCommand(() => Run(PreviousAsync));
        FavoriteCommand = new RelayCommand(() => Run(ToggleFavoriteAsync)); ModeCommand = new RelayCommand(CycleMode); MuteCommand = new RelayCommand(ToggleMute); ShuffleCommand = new RelayCommand(() => { Settings.Mode = Settings.Mode == PlayMode.Shuffle ? PlayMode.Sequential : PlayMode.Shuffle; Changed(nameof(ModeText)); Changed(nameof(ModeIcon)); SaveSettings(); });
        _timer.Interval = TimeSpan.FromMilliseconds(500); _timer.Tick += Tick;
        _saveTimer.Interval = TimeSpan.FromSeconds(1); _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };
        Player.Loaded += () => _dispatcher.BeginInvoke(() => Run(OnLoadedAsync));
        Player.Ended += code => _dispatcher.BeginInvoke(() => { if (code == 0) Run(() => NextAsync(true)); else Run(ReconnectAsync); });
    }
    public async Task ConnectAsync() { Status = "正在连接 Navidrome…"; var version = await Api.PingAsync(); Status = $"已连接 · {version}"; Store.Log("INFO", "Navidrome 连接成功"); }
    public void Run(Func<Task> action) => _ = Guard(action);
    private async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Status = e is ApiException ? e.Message : "操作失败，请重试或查看日志。"; Store.Log("ERROR", $"操作失败 {e.GetType().Name}"); }
    }
    public void UpdateSettings(AppSettings updated, string password)
    {
        Player.Stop(); _loaded = false; _timer.Stop(); _playCts.Cancel();
        bool otherServer = !string.Equals(Settings.Server, updated.Server, StringComparison.OrdinalIgnoreCase) || Settings.Username != updated.Username;
        foreach (var prop in typeof(AppSettings).GetProperties()) prop.SetValue(Settings, prop.GetValue(updated));
        Password = password; Api.Configure(Settings, password);
        Player.Set("tls-verify", Settings.AllowUntrustedCertificate ? "no" : "yes"); Player.Set("audio-exclusive", Settings.Exclusive ? "yes" : "no");
        Player.Volume = Settings.Volume; Player.Set("mute", Settings.Mute ? "yes" : "no");
        if (otherServer) { Queue.Clear(); Index = -1; Results.Clear(); Library.Clear(); Cover = null; SaveQueue(); RefreshTrack(); }
        Changed(nameof(Volume)); Changed(nameof(MuteText)); Changed(nameof(PlayGlyph)); Changed(nameof(ServerConfigured)); SaveSettings(); Theme.Apply(Settings.Theme);
    }
    private void SaveSettingsSoon() { _saveTimer.Stop(); _saveTimer.Start(); }
    public void SaveSettings() => Store.Write("config.json", Settings);
    public void SaveQueue() => Store.Write("queue.json", new QueueState { Tracks = Queue.ToList(), Index = Index, Position = Position });
    private void RefreshTrack() { Changed(nameof(Current)); Changed(nameof(Title)); Changed(nameof(Artist)); Changed(nameof(Album)); Changed(nameof(FavoriteGlyph)); Changed(nameof(PlayGlyph)); }
    public async Task PlayAsync(int index, bool resume = false)
    {
        if (index < 0 || index >= Queue.Count) return;
        _playCts.Cancel(); _playCts.Dispose(); _playCts = new(); var ct = _playCts.Token; var generation = ++_generation;
        Player.Stop(); _timer.Stop(); _loaded = false; _reconnecting = false; _recoveryLoad = false; Index = index; Position = 0; Duration = Current!.Duration; Changed(nameof(Duration));
        _listened = 0; _submitted = false; Cover = null; _coverPath = null; Quality = "正在核实音频格式"; AudioInfo = ""; _responseType = null; RefreshTrack(); SaveQueue();
        var track = Current; Status = $"正在加载 · {track.Title}";
        if (track.IsLocal) { if (!File.Exists(track.LocalPath)) throw new ApiException("找不到音乐文件，请重新打开。"); Player.Load(track.LocalPath!, resume ? _restorePosition : 0); }
        else { if (!Api.Configured) throw new ApiException("请先在设置中连接 Navidrome。"); Player.Load(Api.Url("stream", ("id", track.Id)), resume ? _restorePosition : 0); }
        _restorePosition = 0;
        _ = LoadExtrasAsync(track, generation, ct);
        await Task.CompletedTask;
    }
    private async Task LoadExtrasAsync(Track track, int generation, CancellationToken ct)
    {
        try
        {
            var path = track.IsLocal ? new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }.Select(name => Path.Combine(Path.GetDirectoryName(track.LocalPath!)!, name)).FirstOrDefault(File.Exists) : await Covers.GetPathAsync(track.CoverArt, Settings.CoverCacheMb, ct);
            if (generation != _generation || ct.IsCancellationRequested) return; _coverPath = path; Cover = CoverCacheService.Load(path);
            if (Media != null) await Media.UpdateAsync(track, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Store.Log("WARNING", $"封面或媒体信息更新失败 {e.GetType().Name}"); }
        if (track.IsLocal) return;
        try { var type = await Api.ProbeTypeAsync(track.Id, ct); if (generation == _generation && !ct.IsCancellationRequested) { _responseType = type; UpdateAudioInfo(); } }
        catch (OperationCanceledException) { }
        catch (Exception e) { Store.Log("WARNING", $"响应格式检查失败 {e.GetType().Name}"); }
    }
    private async Task OnLoadedAsync()
    {
        if (_disposed || Current == null) return; _loaded = true; _reconnecting = false; _lastTick = Stopwatch.GetTimestamp(); _timer.Start();
        if (Current.IsLocal) { foreach (var field in new[] { "title", "artist", "album" }) { var value = Player.Get("metadata/by-key/" + field); if (!string.IsNullOrWhiteSpace(value)) { if (field == "title") Current.Title = value; else if (field == "artist") Current.Artist = value; else Current.Album = value; } } Current.NotifyMetadata(); RefreshTrack(); SaveLocalLibrary(); }
        Status = "正在播放"; Duration = Player.Duration > 0 ? Player.Duration : Current.Duration; if (Current.IsLocal) { Current.Duration = Duration; Current.NotifyMetadata(); SaveLocalLibrary(); }
        Changed(nameof(Duration)); Changed(nameof(PlayGlyph)); UpdateAudioInfo();
        Media?.Status(false, false);
        if (!_recoveryLoad) { if (Media != null) await Media.UpdateAsync(Current, _coverPath); if (!Current.IsLocal) await Api.ScrobbleAsync(Current.Id, false); ((App)Application.Current).NotifyTrack(Current); }
        _recoveryLoad = false;
    }
    private static string Normalize(string v) => v.ToLowerInvariant() switch { "ape" or "monkey's audio" => "ape", "aac" or "m4a" => "aac", "alac" => "alac", "mp3" or "mp2" => "mp3", "pcm_s16le" or "pcm_s24le" or "pcm_f32le" or "wav" => "pcm", "vorbis" or "ogg" => "vorbis", _ => v.ToLowerInvariant() };
    private void UpdateAudioInfo()
    {
        if (Current == null) return; var codec = Player.Get("audio-codec-name"); var expected = Normalize(Current.Suffix ?? ""); var actual = Normalize(codec);
        bool containerAmbiguous = expected is "aac" or "vorbis" or "dsf" or "dff";
        bool mismatch = codec.Length > 0 && !containerAmbiguous && expected.Length > 0 && actual != expected;
        bool httpMismatch = _responseType != null && Current.ContentType != null && !string.Equals(_responseType, Current.ContentType, StringComparison.OrdinalIgnoreCase) && !(_responseType == "application/octet-stream");
        Quality = Current.IsLocal ? "本地播放" : mismatch || httpMismatch ? "服务端转码" : codec.Length > 0 && expected.Length > 0 && !containerAmbiguous && actual == expected ? "Direct Play" : "播放中";
        var rate = Player.Get("audio-params/samplerate"); var sampleFormat = Player.Get("audio-params/format"); var channels = Player.Get("audio-params/channel-count");
        static string Value(int n, string unit) => n > 0 ? $"{n} {unit}" : "—";
        AudioInfo = $"格式：{Current.Format}\n采样率：{(Current.SamplingRate > 0 ? Value(Current.SamplingRate, "Hz") : rate.Length > 0 ? rate + " Hz" : "—")}\n位深：{Value(Current.BitDepth, "bit")}\n声道：{(Current.ChannelCount > 0 ? Current.ChannelCount.ToString() : channels.Length > 0 ? channels : "—")}\n码率：{Value(Current.BitRate, "kbps")}\n播放：{Quality}\n解码：{(codec.Length > 0 ? codec.ToUpperInvariant() : "—")}\n输出：WASAPI{(Settings.Exclusive ? " 独占" : " 共享")}";
        if (!Current.IsLocal && (mismatch || httpMismatch)) Store.Log("WARNING", "服务器返回的 Content-Type / 编码与源文件不同，可能发生服务器端转码。");
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (!_loaded || Current == null) return;
        var now = Stopwatch.GetTimestamp(); double elapsed = (now - _lastTick) / (double)Stopwatch.Frequency; _lastTick = now;
        Position = Player.Position; Duration = Player.Duration > 0 ? Player.Duration : Current.Duration; Changed(nameof(Duration)); Changed(nameof(PlayGlyph)); Changed(nameof(PlayIcon)); Changed(nameof(PositionText)); Changed(nameof(DurationText));
        bool paused = Player.IsPaused; bool idle = Player.IsIdle; Media?.Status(idle, paused);
        if (!paused && !idle && Player.Get("paused-for-cache") != "yes") _listened += Math.Min(elapsed, 2);
        if (!Current.IsLocal && !_submitted && Duration > 0 && _listened >= Math.Min(Duration / 2, 240)) { _submitted = true; var id = Current.Id; Run(() => Api.ScrobbleAsync(id, true)); }
        _timer.Interval = TimeSpan.FromMilliseconds(Application.Current.Windows.Cast<Window>().Any(w => w.IsVisible) ? 500 : 1000);
    }
    public async Task ToggleAsync()
    {
        if (Player.IsIdle) { if (Current != null) await PlayAsync(Index, true); else if (Queue.Count > 0) await PlayAsync(0); else await RandomAsync(false); }
        else if (Player.IsPaused) Player.Resume(); else Player.Pause(); Changed(nameof(PlayGlyph)); Media?.Status(Player.IsIdle, Player.IsPaused);
    }
    public void Stop() { _playCts.Cancel(); _loaded = false; _reconnecting = false; _timer.Stop(); Player.Stop(); Position = 0; _restorePosition = 0; Changed(nameof(PlayGlyph)); Media?.Status(true, false); Status = "已停止"; SaveQueue(); }
    public void Seek(double position) { if (!Player.IsIdle) { Player.Seek(position); Position = position; SaveQueue(); } }
    public async Task NextAsync(bool automatic)
    {
        if (Queue.Count == 0) { Stop(); return; }
        int next = Index + 1;
        if (automatic && Settings.Mode == PlayMode.RepeatOne) next = Index;
        else if (Settings.Mode == PlayMode.Shuffle && Queue.Count > 1) { next = Random.Shared.Next(Queue.Count - 1); if (next >= Index) next++; }
        if (next >= Queue.Count) { if (Settings.Mode is PlayMode.RepeatAll or PlayMode.Shuffle) next = 0; else { Stop(); Status = "队列播放完毕"; return; } }
        await PlayAsync(Math.Max(0, next));
    }
    public Task PreviousAsync() { if (Player.Position > 3) { Seek(0); return Task.CompletedTask; } return PlayAsync(Index > 0 ? Index - 1 : 0); }
    public void CycleMode() { Settings.Mode = (PlayMode)(((int)Settings.Mode + 1) % 4); Changed(nameof(ModeText)); Changed(nameof(ModeIcon)); SaveSettings(); }
    public void ToggleMute() { Settings.Mute = !Settings.Mute; Player.Set("mute", Settings.Mute ? "yes" : "no"); Changed(nameof(MuteText)); SaveSettings(); }
    public async Task ToggleFavoriteAsync()
    {
        var track = Current; if (track == null) return; bool star = track.Starred == null; if (!track.IsLocal) await Api.StarAsync(track.Id, star);
        foreach (var t in Queue.Concat(Results).Concat(_localTracks).Where(t => t.Id == track.Id)) t.Starred = star ? DateTime.UtcNow.ToString("O") : null;
        Changed(nameof(FavoriteGlyph)); SaveQueue(); if (track.IsLocal) SaveLocalLibrary(); Status = star ? "已收藏" : "已取消收藏";
    }
    public async Task RandomAsync(bool favorites)
    {
        Status = "正在获取随机歌曲…"; var tracks = Navigation == "本地音乐" || !Api.Configured ? _localTracks.Where(t => !favorites || t.Starred != null).ToList() : favorites ? await Api.StarredAsync() : await Api.RandomAsync();
        if (favorites || Navigation == "本地音乐" || !Api.Configured) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tracks));
        if (tracks.Count == 0) { Status = favorites ? "还没有收藏歌曲" : "音乐库中没有歌曲"; return; }
        Stop(); Queue.Clear(); foreach (var t in tracks.Take(100)) Queue.Add(t); Index = -1; await PlayAsync(0);
    }
    public async Task PlayTrackAsync(Track track) { Queue.Add(track); await PlayAsync(Queue.Count - 1); }
    public void Add(Track track, bool next) { if (next && Index >= 0) Queue.Insert(Index + 1, track); else Queue.Add(track); SaveQueue(); Status = next ? "已加入下一首" : "已加入队列"; }
    public void Move(int from, int to) { if (from < 0 || to < 0 || from >= Queue.Count || to >= Queue.Count) return; var current = Current; Queue.Move(from, to); Index = current == null ? -1 : Queue.IndexOf(current); SaveQueue(); RefreshTrack(); }
    public void Remove(int index)
    {
        if (index < 0 || index >= Queue.Count) return; bool current = index == Index; var active = Current;
        if (current) Stop(); Queue.RemoveAt(index); Index = current ? -1 : active == null ? -1 : Queue.IndexOf(active); RefreshTrack(); SaveQueue();
    }
    public void ClearQueue() { Stop(); Queue.Clear(); Index = -1; Cover = null; Quality = "尚未播放"; RefreshTrack(); SaveQueue(); }
    public async Task BrowseAsync(string section, string query = "", LibraryItem? item = null)
    {
        _browseCts.Cancel(); _browseCts.Dispose(); _browseCts = new(); var ct = _browseCts.Token; var generation = ++_browseGeneration;
        if (item == null && section != "搜索") Navigation = section;
        Section = section; Status = "正在加载…"; JsonElementResult result = await FetchAsync(section, query, item, ct); if (ct.IsCancellationRequested || generation != _browseGeneration) return;
        Results.Clear(); Library.Clear(); foreach (var t in result.Tracks) Results.Add(t); foreach (var row in result.Items) Library.Add(row);
        Status = $"{section} · {Results.Count} 首歌曲，{Library.Count} 个项目";
        _ = LoadLibraryCoversAsync(generation, ct);
    }
    private async Task LoadLibraryCoversAsync(int generation, CancellationToken ct)
    {
        foreach (var item in Library.Take(16).ToArray()) try { var path = await Covers.GetPathAsync(item.CoverArt, Settings.CoverCacheMb, ct); if (ct.IsCancellationRequested || generation != _browseGeneration) return; item.Cover = CoverCacheService.Load(path, 160); } catch (OperationCanceledException) { return; } catch (Exception e) { Store.Log("WARNING", $"列表封面读取失败 {e.GetType().Name}"); }
    }
    private sealed record JsonElementResult(List<Track> Tracks, List<LibraryItem> Items);
    private async Task<JsonElementResult> FetchAsync(string section, string query, LibraryItem? item, CancellationToken ct)
    {
        if (section == "本地音乐") return new(_localTracks.ToList(), []);
        if (section == "搜索" && (Navigation == "本地音乐" || !Api.Configured)) return new(_localTracks.Where(t => ($"{t.Title} {t.Artist} {t.Album}").Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList(), []);
        if (section == "收藏") return new((Api.Configured ? await Api.StarredAsync() : []).Concat(_localTracks.Where(t => t.Starred != null)).ToList(), []);
        if (section == "随机") return new(Api.Configured ? await Api.RandomAsync() : _localTracks.OrderBy(_ => Random.Shared.Next()).ToList(), []);
        if (!Api.Configured) return new([], []);
        string endpoint = section switch { "搜索" => "search3", "歌单" => "getPlaylists", "歌手" => "getArtists", _ => "getAlbumList2" };
        List<(string Key, string Value)> args = [];
        if (item != null) { endpoint = item.Kind switch { "artist" => "getArtist", "playlist" => "getPlaylist", _ => "getAlbum" }; args.Add(("id", item.Id)); }
        else if (endpoint == "search3") { args.Add(("query", query)); args.Add(("songCount", "50")); args.Add(("albumCount", "20")); args.Add(("artistCount", "20")); }
        else if (endpoint == "getAlbumList2") { args.Add(("type", section == "最近播放" ? "recent" : "newest")); args.Add(("size", "40")); }
        var root = await Api.CallAsync(endpoint, ct, args.ToArray()); List<Track> songs = []; List<LibraryItem> items = [];
        void Rows(System.Text.Json.JsonElement parent, string key, string kind)
        { if (parent.TryGetProperty(key, out var arr)) foreach (var row in arr.EnumerateArray()) items.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), NavidromeApiClient.Text(row, "artist", kind == "playlist" ? "歌单" : kind == "artist" ? "歌手" : "专辑"), kind, NavidromeApiClient.Text(row, "coverArt"))); }
        if (endpoint == "search3" && root.TryGetProperty("searchResult3", out var search)) { songs = NavidromeApiClient.Tracks(root, "searchResult3"); Rows(search, "album", "album"); Rows(search, "artist", "artist"); }
        if (endpoint == "getAlbum") songs = NavidromeApiClient.Tracks(root, "album");
        if (endpoint == "getPlaylist") songs = NavidromeApiClient.Tracks(root, "playlist", "entry");
        if (endpoint == "getArtist" && root.TryGetProperty("artist", out var artist)) Rows(artist, "album", "album");
        if (endpoint == "getPlaylists" && root.TryGetProperty("playlists", out var playlists)) Rows(playlists, "playlist", "playlist");
        if (endpoint == "getAlbumList2" && root.TryGetProperty("albumList2", out var albums)) Rows(albums, "album", "album");
        if (endpoint == "getArtists" && root.TryGetProperty("artists", out var artists) && artists.TryGetProperty("index", out var indexes)) foreach (var group in indexes.EnumerateArray()) Rows(group, "artist", "artist");
        return new(songs, items);
    }
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
                try { await Api.PingAsync(ct); if (generation != _generation) return; _recoveryLoad = true; Player.Load(Api.Url("stream", ("id", Current.Id)), start); if (paused) Player.Pause(); Status = "正在重新加载音频流…"; return; }
                catch (ApiException) { Store.Log("WARNING", "网络重连失败"); }
            }
        }
        finally { _reconnecting = false; }
    }
    public static readonly HashSet<string> LocalExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".ape", ".aac", ".m4a", ".alac", ".wav", ".ogg", ".opus", ".wma", ".dsf", ".dff", ".aif", ".aiff", ".wv", ".tta", ".tak", ".mpc", ".ac3", ".dts", ".cue" };
    private void SaveLocalLibrary() => Store.Write("local-library.json", _localTracks);
    public async Task OpenLocalFilesAsync(IEnumerable<string> paths) { var added = new List<Track>(); foreach (var path in paths) { if (!File.Exists(path) || !LocalExtensions.Contains(Path.GetExtension(path))) continue; string full = Path.GetFullPath(path); var track = _localTracks.FirstOrDefault(t => string.Equals(t.LocalPath, full, StringComparison.OrdinalIgnoreCase)); if (track == null) { track = new Track { Id = "local:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full.ToUpperInvariant()))), LocalPath = full, Title = Path.GetFileNameWithoutExtension(full), Artist = "本地音乐", Album = "本地音乐", Suffix = Path.GetExtension(full).TrimStart('.') }; _localTracks.Add(track); } if (!added.Any(t => t.Id == track.Id)) added.Add(track); } if (added.Count == 0) { Status = "没有可播放的音乐文件。"; return; } SaveLocalLibrary(); await BrowseAsync("本地音乐"); int first = Queue.Count; foreach (var track in added) Queue.Add(track); await PlayAsync(first); }
    public void Dispose() { if (_disposed) return; SaveQueue(); SaveSettings(); _disposed = true; _playCts.Cancel(); _browseCts.Cancel(); _timer.Stop(); _saveTimer.Stop(); Media?.Dispose(); Player.Dispose(); Api.Dispose(); _playCts.Dispose(); _browseCts.Dispose(); }
}
