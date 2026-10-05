using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ChiliMusic;

internal sealed partial class RemoteControlService : IDisposable
{
    private readonly PlayerViewModel _vm;
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private LanHttpServer? _server;
    private readonly Dictionary<string, DateTime> _sessions = [];
    private readonly Dictionary<string, Track> _tracks = [];
    private readonly Dictionary<Track, string> _keys = [];
    private readonly Dictionary<string, LibraryItem> _albums = [];
    private readonly object _auth = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private int _attempts, _revision;
    private DateTime _attemptWindow = DateTime.UtcNow;
    private string _code = "";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public bool Running => _server != null;
    public int Port => _server?.Port ?? 47831;
    public string PairCode { get { lock (_auth) return _code; } }
    public int Devices { get { lock (_auth) { PruneSessions(); return _sessions.Count; } } }
    public RemoteControlService(PlayerViewModel vm) { _vm = vm; vm.Queue.CollectionChanged += QueueChanged; }
    private void QueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => _revision++;
    public void Start(int port = 47831)
    {
        if (Running) return;
        var server = new LanHttpServer(RouteAsync); server.Start(port); ResetPairing(); _server = server;
    }
    public void Stop() { if (!((App)Application.Current).Exiting && _vm.MobileOutput) { _vm.PausePlayback(); _vm.Run(() => _vm.SetMobileOutputAsync(false)); } _mobileOwner = null; _server?.Dispose(); _server = null; lock (_auth) { _sessions.Clear(); _code = ""; } _tracks.Clear(); _keys.Clear(); _albums.Clear(); }
    public void ResetPairing()
    {
        if (_vm.MobileOutput) { _vm.PausePlayback(); _vm.Run(() => _vm.SetMobileOutputAsync(false)); }
        _mobileOwner = null;
        lock (_auth) { _sessions.Clear(); _attempts = 0; _attemptWindow = DateTime.UtcNow; _code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); }
    }
    public static IReadOnlyList<string> Addresses() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && LanHttpServer.IsLocalAddress(a) && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).Distinct().OrderBy(a => a.StartsWith("169.254.")).ToArray();
    private void PruneSessions() { foreach (var token in _sessions.Where(s => DateTime.UtcNow - s.Value > TimeSpan.FromHours(12)).Select(s => s.Key).ToArray()) _sessions.Remove(token); }
    private bool Authorized(LanRequest request)
    {
        lock (_auth)
        {
            PruneSessions(); if (!request.Headers.TryGetValue("X-Chili-Token", out var token) || !_sessions.ContainsKey(token)) return false;
            _sessions[token] = DateTime.UtcNow; return true;
        }
    }
    private static LanResponse Result(object value, int status = 200) => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, Json));
    private static LanResponse Error(string message, int status) => Result(new { error = message }, status);
    private Task<T> Ui<T>(Func<T> work) => _ui.InvokeAsync(work).Task;
    private Task<T> UiAsync<T>(Func<Task<T>> work) => _ui.InvokeAsync(work).Task.Unwrap();
    private async Task<LanResponse> RouteAsync(LanRequest request, CancellationToken ct)
    {
        var uri = new Uri("http://localhost" + request.Target); string path = uri.AbsolutePath;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).GroupBy(p => Uri.UnescapeDataString(p[0])).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First().Length == 2 ? g.First()[1].Replace('+', ' ') : ""));
        if (request.Method == "GET" && path is "/" or "/index.html" or "/app.js" or "/style.css")
        {
            string name = path == "/" ? "index.html" : path.TrimStart('/');
            using var resource = typeof(RemoteControlService).Assembly.GetManifestResourceStream("chilimusic.RemoteWeb." + name) ?? typeof(RemoteControlService).Assembly.GetManifestResourceStream("ChiliMusic.RemoteWeb." + name);
            if (resource == null) return Error("页面不存在", 404);
            using var memory = new MemoryStream(); await resource.CopyToAsync(memory, ct);
            return new(200, name.EndsWith(".js") ? "text/javascript; charset=utf-8" : name.EndsWith(".css") ? "text/css; charset=utf-8" : "text/html; charset=utf-8", memory.ToArray());
        }
        if (path == "/api/info" && request.Method == "GET") return Result(await Ui(() => new { colors = Theme.WebColors(), theme = Theme.Current.Name, dark = Theme.IsDark }));
        if (request.Method == "POST" && (!request.Headers.TryGetValue("Content-Type", out var type) || !type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))) return Error("请使用网页控制", 400);
        if (path == "/api/pair" && request.Method == "POST") return Pair(request);
        if (!path.StartsWith("/api/")) return Error("页面不存在", 404);
        if (path == "/api/audio" && request.Method == "GET") return await AudioAsync(request, query, ct);
        if (!Authorized(request)) return Error("请先配对", 401);
        try
        {
            if (path == "/api/state" && request.Method == "GET") return Result(await Ui(() => State(request.Headers["X-Chili-Token"])));
            if (path == "/api/lyrics" && request.Method == "GET") return Result(await Ui(() => new { key = _vm.Current == null ? "" : Key(_vm.Current), revision = _vm.LyricsRevision, status = _vm.LyricsStatus, offset = _vm.LyricOffset, lines = _vm.LyricLines.Take(4000).Select(l => new { time = l.Time, text = l.Text, translation = l.Translation }).ToArray() }));
            if (path == "/api/queue" && request.Method == "GET") return Result(await Ui(() => { int offset = Offset(query); return new { revision = _revision, total = _vm.Queue.Count, offset, tracks = _vm.Queue.Skip(offset).Take(100).Select((t, i) => Song(t, offset + i)).ToArray() }; }));
            if (path == "/api/library" && request.Method == "GET") return Result(await UiAsync(() => LibraryAsync(query, ct)));
            if (path == "/api/album" && request.Method == "GET") return Result(await UiAsync(() => AlbumAsync(query, ct)));
            if (path == "/api/cover" && request.Method == "GET")
            {
                var bytes = await UiAsync(() => CoverAsync(query, ct)); return bytes == null ? Error("暂无封面", 404) : new(200, "image/png", bytes);
            }
            if (path == "/api/logout" && request.Method == "POST")
            {
                string token = request.Headers["X-Chili-Token"];
                await UiAsync(async () => { if (_mobileOwner == token) { _vm.PausePlayback(); await _vm.SetMobileOutputAsync(false); _mobileOwner = null; } return true; });
                lock (_auth) _sessions.Remove(token);
                return Result(new { ok = true });
            }
            if (path == "/api/command" && request.Method == "POST")
            {
                using var document = JsonDocument.Parse(request.Body); var command = document.RootElement.Clone();
                await _commands.WaitAsync(ct);
                try { return Result(await UiAsync(async () => { await CommandAsync(command, request.Headers["X-Chili-Token"]); return State(request.Headers["X-Chili-Token"]); })); } finally { _commands.Release(); }
            }
            return Error("页面不存在", 404);
        }
        catch (OperationCanceledException) { throw; }
        catch (RemoteConflictException) { return Error("队列已变化，请刷新后重试", 409); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or ApiException or KeyNotFoundException)
        { return Error(e is ApiException ? "曲库操作未完成，请检查电脑端连接" : "操作未完成，请刷新后重试", 400); }
    }
    private LanResponse Pair(LanRequest request)
    {
        lock (_auth)
        {
            if (DateTime.UtcNow - _attemptWindow >= TimeSpan.FromMinutes(1)) { _attemptWindow = DateTime.UtcNow; _attempts = 0; }
            if (_attempts >= 5) return Error("尝试次数较多，请稍后再试", 429);
            try
            {
                using var document = JsonDocument.Parse(request.Body); string code = document.RootElement.GetProperty("code").GetString() ?? "";
                if (code.Length != 6 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(_code))) { _attempts++; return Error("配对码不正确", 401); }
                PruneSessions(); if (_sessions.Count >= 16) return Error("已连接设备较多，请在电脑端重新配对", 429);
                string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); _sessions[token] = DateTime.UtcNow;
                var response = Result(new { token }); return response with { Headers = new() { ["Set-Cookie"] = $"ChiliRemote={token}; Path=/api/audio; HttpOnly; SameSite=Strict" } };
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { _attempts++; return Error("请输入六位配对码", 400); }
        }
    }
    private string Key(Track track)
    {
        if (_keys.TryGetValue(track, out var key)) return key;
        if (_tracks.Count >= 12000) { foreach (var stale in _keys.Keys.Where(t => !_vm.Queue.Contains(t)).Take(2000).ToArray()) { _tracks.Remove(_keys[stale]); _keys.Remove(stale); } }
        if (_tracks.Count >= 14000) throw new ApiException("曲库较大，请重新连接手机遥控。");
        key = Guid.NewGuid().ToString("N"); _keys[track] = key; _tracks[key] = track; return key;
    }
    private object Song(Track track, int? index = null) => new { key = Key(track), index, title = track.Title, artist = track.Artist, album = track.Album, duration = track.Duration, format = track.Format, preview = track.Preview, local = track.IsLocal, favorite = track.Starred != null, cover = track.IsLocal ? track.EmbeddedCoverPath != null || _vm.Current == track && _vm.Cover != null : !string.IsNullOrEmpty(track.CoverArt) };
    private object State(string token) => new { current = _vm.Current == null ? null : Song(_vm.Current, _vm.Index), playing = _vm.MobileOutput ? _vm.MobilePlaying : !_vm.Player.IsIdle && !_vm.Player.IsPaused, idle = _vm.Current == null || !_vm.MobileOutput && _vm.Player.IsIdle, position = _vm.Position, duration = _vm.Duration, volume = _vm.Volume, mute = _vm.Settings.Mute, mode = (int)_vm.Settings.Mode, modeText = _vm.ModeText, revision = _revision, queueTotal = _vm.Queue.Count, next = _vm.Queue.Skip(Math.Max(0, _vm.Index + 1)).Take(3).Select((t, i) => Song(t, Math.Max(0, _vm.Index + 1) + i)).ToArray(), colors = Theme.WebColors(), theme = Theme.Current.Name, dark = Theme.IsDark, seekRevision = _vm.SeekRevision, lyricsRevision = _vm.LyricsRevision, lyricOffset = _vm.LyricOffset, server = _vm.Api.NavidromeConfigured, netease = true, sleep = _vm.SleepStatus, output = _vm.MobileOutput ? "phone" : "computer", phoneOwner = token == _mobileOwner };
    private static int Offset(Dictionary<string, string> query) => query.TryGetValue("offset", out var value) && int.TryParse(value, out var offset) ? Math.Clamp(offset, 0, 100000) : 0;
    private async Task<object> LibraryAsync(Dictionary<string, string> query, CancellationToken ct)
    {
        string source = query.GetValueOrDefault("source", _vm.ServerConfigured ? "server" : "local"), term = query.GetValueOrDefault("query", ""), view = query.GetValueOrDefault("view", "newest"); int offset = Offset(query);
        if (term.Length > 120) throw new ArgumentException();
        if (source is "local" or "offline" or "favorite")
        {
            var all = source == "offline" ? _vm.OfflineEntries.Select(e => e.Track) : _vm.LocalLibrary.Snapshot.AsEnumerable();
            if (source == "favorite") all = all.Where(t => t.Starred != null);
            var matches = all.Where(t => (t.Title + " " + t.Artist + " " + t.Album).Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            return new { tracks = matches.Skip(offset).Take(50).Select(t => Song(t)).ToArray(), albums = Array.Empty<object>(), more = offset + 50 < matches.Length, offset };
        }
        if (source is not ("server" or "netease") || source == "server" && !_vm.Api.NavidromeConfigured) throw new ApiException("服务器未连接");
        if (term.Length == 0 && source == "netease" && view is "recent" or "favorites" or "daily")
        {
            string endpoint = view == "recent" ? "getRecentSongs" : view == "daily" ? "getDailyRecommendations" : "getStarred2";
            string container = view == "recent" ? "recentSongs" : view == "daily" ? "randomSongs" : "starred2";
            var root = await _vm.Api.CallSourceAsync(source, endpoint, ct); var songs = NavidromeApiClient.Tracks(root, container);
            return new { tracks = songs.Skip(offset).Take(50).Select(t => Song(t)).ToArray(), albums = Array.Empty<object>(), more = offset + 50 < songs.Count, offset };
        }
        if (term.Length > 0)
        {
            var root = await _vm.Api.CallSourceAsync(source, "search3", ct, ("query", term), ("songCount", "50"), ("songOffset", offset.ToString()), ("albumCount", "0"), ("artistCount", "0")); var songs = NavidromeApiClient.Tracks(root, "searchResult3");
            return new { tracks = songs.Select(t => Song(t)).ToArray(), albums = Array.Empty<object>(), more = songs.Count == 50, offset };
        }
        var result = await _vm.Api.CallSourceAsync(source, "getAlbumList2", ct, ("type", view == "recent" ? "recent" : "newest"), ("size", "50"), ("offset", offset.ToString())); var albums = new List<object>();
        if (result.TryGetProperty("albumList2", out var list) && list.TryGetProperty("album", out var rows)) foreach (var row in rows.EnumerateArray())
        {
            var album = new LibraryItem(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), NavidromeApiClient.Text(row, "artist"), "album", NavidromeApiClient.Text(row, "coverArt"));
            string key = Guid.NewGuid().ToString("N"); if (_albums.Count >= 1000) _albums.Remove(_albums.Keys.First()); _albums[key] = album; albums.Add(new { key, title = album.Title, artist = album.Subtitle, cover = !string.IsNullOrEmpty(album.CoverArt) });
        }
        return new { tracks = Array.Empty<object>(), albums, more = albums.Count == 50, offset };
    }
    private async Task<object> AlbumAsync(Dictionary<string, string> query, CancellationToken ct)
    {
        if (!_albums.TryGetValue(query.GetValueOrDefault("key", ""), out var album)) throw new KeyNotFoundException();
        var root = await _vm.Api.CallAsync("getAlbum", ct, ("id", album.Id)); var tracks = NavidromeApiClient.Tracks(root, "album");
        return new { title = album.Title, tracks = tracks.Take(1000).Select(t => Song(t)).ToArray(), albums = Array.Empty<object>(), more = false, offset = 0 };
    }
    private async Task<byte[]?> CoverAsync(Dictionary<string, string> query, CancellationToken ct)
    {
        string key = query.GetValueOrDefault("key", ""); BitmapSource? image = null;
        if (_tracks.TryGetValue(key, out var track))
        {
            if (track == _vm.Current) image = _vm.Cover as BitmapSource;
            if (image == null) { string? path = track.IsLocal ? track.EmbeddedCoverPath ?? new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }.Select(n => Path.Combine(Path.GetDirectoryName(track.LocalPath!)!, n)).FirstOrDefault(File.Exists) : await _vm.Covers.GetPathAsync(track.CoverArt, _vm.Settings.CoverCacheMb, ct); image = await Task.Run(() => CoverCacheService.Load(path, 420), ct); }
        }
        else if (_albums.TryGetValue(key, out var album)) { var path = await _vm.Covers.GetPathAsync(album.CoverArt, _vm.Settings.CoverCacheMb, ct); image = await Task.Run(() => CoverCacheService.Load(path, 160), ct); }
        if (image == null) return null; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var memory = new MemoryStream(); encoder.Save(memory); return memory.ToArray();
    }
    private async Task CommandAsync(JsonElement command, string token)
    {
        string action = command.GetProperty("action").GetString() ?? "";
        double Number() { double value = command.GetProperty("value").GetDouble(); if (!double.IsFinite(value)) throw new ArgumentException(); return value; }
        int QueueIndex() { if (command.GetProperty("revision").GetInt32() != _revision) throw new RemoteConflictException(); int index = command.GetProperty("index").GetInt32(); if (index < 0 || index >= _vm.Queue.Count) throw new ArgumentException(); return index; }
        Track Selected() => _tracks[command.GetProperty("key").GetString() ?? ""];
        switch (action)
        {
            case "toggle": await _vm.ToggleAsync(); break;
            case "play": await _vm.ResumePlaybackAsync(); break;
            case "pause": _vm.PausePlayback(); break;
            case "output-phone": await _vm.SetMobileOutputAsync(true); _mobileOwner = token; break;
            case "output-computer": if (token == _mobileOwner && command.TryGetProperty("value", out _)) _vm.UpdateMobilePosition(Number()); await _vm.SetMobileOutputAsync(false); _mobileOwner = null; break;
            case "phone-position": if (token == _mobileOwner && _vm.Current != null && command.GetProperty("key").GetString() == Key(_vm.Current)) _vm.UpdateMobilePosition(Number()); break;
            case "phone-ended": if (token == _mobileOwner && _vm.MobileOutput && _vm.MobilePlaying && _vm.Current != null && command.GetProperty("key").GetString() == Key(_vm.Current)) await _vm.NextAsync(true); break;
            case "next": await _vm.NextAsync(false); break;
            case "previous": await _vm.PreviousAsync(); break;
            case "seek": _vm.Seek(Math.Clamp(Number(), 0, _vm.Duration)); break;
            case "volume": _vm.Volume = Number(); break;
            case "mute": _vm.ToggleMute(); break;
            case "mode": _vm.CycleMode(); break;
            case "favorite": await _vm.ToggleFavoriteAsync(); break;
            case "queue-play": await _vm.PlayAsync(QueueIndex()); break;
            case "queue-remove": _vm.Remove(QueueIndex()); break;
            case "queue-move": int from = QueueIndex(), to = command.GetProperty("to").GetInt32(); if (to < 0 || to >= _vm.Queue.Count) throw new ArgumentException(); _vm.Move(from, to); break;
            case "add": _vm.Add(Selected(), false); break;
            case "add-next": _vm.Add(Selected(), true); break;
            case "play-track": await _vm.PlayTrackAsync(Selected()); break;
            default: throw new ArgumentException();
        }
    }
    public void Dispose() { Stop(); _vm.Queue.CollectionChanged -= QueueChanged; }
    private sealed class RemoteConflictException : Exception { }
}
