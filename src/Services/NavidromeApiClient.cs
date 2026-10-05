using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ChiliMusic;

public sealed class ApiException(string message) : Exception(message);
public sealed class NavidromeApiClient : IDisposable
{
    private HttpClient _http = null!;
    private AppSettings _settings = new();
    private string _password = "";
    public bool Configured => !string.IsNullOrEmpty(_settings.Server) && !string.IsNullOrEmpty(_password);
    public string CacheScope => _settings.Server + "\0" + _settings.Username;
    public NavidromeApiClient(AppSettings settings, string password) => Configure(settings, password);
    public void Configure(AppSettings settings, string password)
    {
        _http?.Dispose(); _settings = settings; _password = password;
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false };
        if (settings.AllowUntrustedCertificate) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; _http.DefaultRequestHeaders.UserAgent.ParseAdd("chilimusic/1.1");
    }
    public static string ValidateServer(string input)
    {
        if (!Uri.TryCreate(input.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ApiException("服务器地址必须是 HTTP/HTTPS 地址，可以包含子路径，不能包含账户、查询参数或片段。");
        return uri.AbsoluteUri.TrimEnd('/');
    }
    public string Url(string endpoint, params (string Key, string Value)[] args)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var token = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(_password + salt))).ToLowerInvariant();
        var pairs = new List<(string, string)> { ("u", _settings.Username), ("t", token), ("s", salt), ("v", "1.16.1"), ("c", "ChiliMusic") };
        if (endpoint is not ("stream" or "download" or "getCoverArt")) pairs.Add(("f", "json"));
        pairs.AddRange(args);
        return ValidateServer(_settings.Server) + "/rest/" + endpoint + ".view?" + string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new ApiException(code is 401 or 403 ? "认证失败或没有访问权限。" : code is >= 300 and < 400 ? "服务器重定向了请求，请填写最终服务地址。" : $"服务器返回 HTTP {code}。"); }
            return response;
        }
        catch (HttpRequestException e) { throw new ApiException(e.InnerException is System.Security.Authentication.AuthenticationException ? "TLS 证书验证失败，请检查证书及主机名。" : "服务器不可访问，请检查地址、网络和代理。"); }
    }
    public async Task<JsonElement> CallAsync(string endpoint, CancellationToken ct = default, params (string Key, string Value)[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url(endpoint, args));
            using var response = await SendAsync(request, timeout.Token);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            if (!json.RootElement.TryGetProperty("subsonic-response", out var root)) throw new ApiException("API 不兼容：响应中没有 subsonic-response。");
            if (root.GetProperty("status").GetString() != "ok")
            {
                int code = root.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                throw new ApiException(code is 40 or 41 ? "认证失败，请检查用户名和密码。" : code == 50 ? "此账号没有操作权限。" : code is 20 or 30 ? "API 版本不兼容。" : $"服务器 API 错误（{code}）。");
            }
            Store.Log("INFO", $"API {endpoint} 成功"); return root.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException("请求超时（10 秒），请检查服务器和网络。"); }
        catch (JsonException) { throw new ApiException("API 不兼容：服务器没有返回有效 JSON，请检查服务地址及反向代理。"); }
    }
    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        var e = await CallAsync("ping", ct); return $"{Text(e, "type", "Subsonic")} {Text(e, "serverVersion", Text(e, "version", ""))}";
    }
    public static string Text(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) ? v.ToString() : fallback;
    public static List<Track> Tracks(JsonElement root, string container, string key = "song") => root.TryGetProperty(container, out var e) && e.TryGetProperty(key, out var array) ? array.EnumerateArray().Select(Track.Parse).ToList() : [];
    public async Task<List<Track>> RandomAsync() => Tracks(await CallAsync("getRandomSongs", default, ("size", "100")), "randomSongs");
    public async Task<List<Track>> StarredAsync() => Tracks(await CallAsync("getStarred2"), "starred2");
    public Task<JsonElement> StarAsync(string id, bool star) => CallAsync(star ? "star" : "unstar", default, ("id", id));
    public Task<JsonElement> ScrobbleAsync(string id, bool submission) => CallAsync("scrobble", default, ("id", id), ("submission", submission ? "true" : "false"), ("time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()));
    public async Task<string?> ProbeTypeAsync(string id, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("stream", ("id", id)));
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var response = await SendAsync(request, timeout.Token); return response.Content.Headers.ContentType?.MediaType;
    }
    public async Task<byte[]> CoverAsync(string id, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("getCoverArt", ("id", id), ("size", "480")));
        using var response = await SendAsync(request, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream();
        byte[] buffer = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(buffer, timeout.Token)) > 0) { if (memory.Length + n > 10 * 1024 * 1024) throw new ApiException("封面超过 10 MB。"); memory.Write(buffer, 0, n); }
        return memory.ToArray();
    }
    public async Task DownloadAsync(string id, string destination, long maxBytes, IProgress<(long Bytes, long? Total)>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("download", ("id", id)));
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(ct); stalled.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await SendAsync(request, stalled.Token);
        string type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        if (type.StartsWith("text/") || type.EndsWith("json") || type.EndsWith("xml")) throw new ApiException("服务器未允许下载这首歌曲。");
        long? total = response.Content.Headers.ContentLength;
        if (total > maxBytes) throw new ApiException("这首歌曲超过离线缓存容量。");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        byte[] buffer = new byte[65536]; long written = 0; int read;
        while ((read = await input.ReadAsync(buffer, stalled.Token)) > 0)
        {
            stalled.CancelAfter(TimeSpan.FromSeconds(30));
            written += read; if (written > maxBytes) throw new ApiException("这首歌曲超过离线缓存容量。");
            await output.WriteAsync(buffer.AsMemory(0, read), ct); progress?.Report((written, total));
        }
        if (written == 0 || total.HasValue && total.Value != written) throw new ApiException("下载未完成，请重试。");
    }
    public async Task<string> CreatePlaylistAsync(string name, IEnumerable<string> songs)
    {
        var args = new List<(string Key, string Value)> { ("name", name) }; args.AddRange(songs.Select(id => ("songId", id)));
        var root = await CallAsync("createPlaylist", default, args.ToArray());
        return root.TryGetProperty("playlist", out var playlist) ? Text(playlist, "id") : "";
    }
    public Task<System.Text.Json.JsonElement> UpdatePlaylistAsync(string id, string? name = null, IEnumerable<string>? add = null, IEnumerable<int>? remove = null)
    {
        var args = new List<(string Key, string Value)> { ("playlistId", id) }; if (name != null) args.Add(("name", name));
        if (add != null) args.AddRange(add.Select(song => ("songIdToAdd", song))); if (remove != null) args.AddRange(remove.Select(index => ("songIndexToRemove", index.ToString())));
        return CallAsync("updatePlaylist", default, args.ToArray());
    }
    public void Dispose() => _http.Dispose();
}
