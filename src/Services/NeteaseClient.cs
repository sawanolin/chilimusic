using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChiliMusic;

public sealed record NeteaseLogin(string ProtectedCookie = "", string DeviceId = "", string UserId = "", string Nickname = "");
public sealed partial class NeteaseClient : IDisposable
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private NeteaseLogin _login = Store.Read("netease-account.json", new NeteaseLogin());
    public string UserId => _login.UserId;
    public string Nickname => _login.Nickname;
    public bool LoggedIn => _cookies.GetCookies(new Uri("https://music.163.com"))["MUSIC_U"] != null;
    public event Action? AccountChanged;
    public NeteaseClient()
    {
        if (string.IsNullOrEmpty(_login.DeviceId)) _login = _login with { DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant() };
        AddCookie("os", "pc"); AddCookie("appver", "3.1.1"); AddCookie("deviceId", _login.DeviceId);
        try { foreach (var part in CredentialService.Unprotect(_login.ProtectedCookie).Split(';')) { int split = part.IndexOf('='); if (split > 0) AddCookie(part[..split].Trim(), part[(split + 1)..].Trim()); } } catch { _login = _login with { ProtectedCookie = "", UserId = "", Nickname = "" }; }
        _http = new(new SocketsHttpHandler { CookieContainer = _cookies, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
    }
    private void AddCookie(string name, string value) { try { _cookies.Add(new Cookie(name, value, "/", ".music.163.com") { Secure = true }); } catch (CookieException) { } }
    private static byte[] Encrypt(string text, string key, bool cbc)
    {
        using var aes = Aes.Create(); aes.Key = Encoding.UTF8.GetBytes(key); aes.Mode = cbc ? CipherMode.CBC : CipherMode.ECB; aes.Padding = PaddingMode.PKCS7;
        if (cbc) aes.IV = Encoding.ASCII.GetBytes("0102030405060708");
        return aes.CreateEncryptor().TransformFinalBlock(Encoding.UTF8.GetBytes(text), 0, Encoding.UTF8.GetByteCount(text));
    }
    internal static Dictionary<string, string> Weapi(string text)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string secret = new(Enumerable.Range(0, 16).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        using var rsa = RSA.Create(); rsa.ImportFromPem("-----BEGIN PUBLIC KEY-----\nMIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDgtQn2JZ34ZC28NWYpAUd98iZ37BUrX/aKzmFbt7clFSs6sXqHauqKWqdtLkF2KexO40H1YTX8z2lSgBBOAxLsvaklV8k4cBFK9snQXE9/DDaFt6Rr7iVZMldczhC0JNgTz+SHXT6CBHuX3e9SdB1Ua44oncaTWz7OBGLbCiK45wIDAQAB\n-----END PUBLIC KEY-----");
        var p = rsa.ExportParameters(false); var m = new BigInteger(Encoding.ASCII.GetBytes(new string(secret.Reverse().ToArray())), true, true);
        var encrypted = BigInteger.ModPow(m, new BigInteger(p.Exponent!, true, true), new BigInteger(p.Modulus!, true, true)).ToByteArray(true, true);
        return new() { ["params"] = Convert.ToBase64String(Encrypt(Convert.ToBase64String(Encrypt(text, "0CoJUm6Qyw8W8jud", true)), secret, true)), ["encSecKey"] = Convert.ToHexString(encrypted).ToLowerInvariant().PadLeft(256, '0') };
    }
    public async Task<JsonElement> RequestAsync(string path, Dictionary<string, object?>? data = null, CancellationToken ct = default, bool eapi = false, bool allowLoginCodes = false)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal)) throw new ArgumentException(nameof(path));
        data = data == null ? [] : new(data); data["csrf_token"] = _cookies.GetCookies(new Uri("https://music.163.com"))["__csrf"]?.Value ?? ""; data["e_r"] = false;
        if (eapi)
        {
            var header = new Dictionary<string, string> { ["os"] = "pc", ["appver"] = "3.1.1", ["deviceId"] = _login.DeviceId, ["requestId"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + RandomNumberGenerator.GetInt32(10000).ToString("D4"), ["__csrf"] = data["csrf_token"]?.ToString() ?? "" };
            foreach (Cookie cookie in _cookies.GetCookies(new Uri("https://music.163.com"))) if (cookie.Name is "MUSIC_U" or "MUSIC_A" or "NMTID") header[cookie.Name] = cookie.Value;
            data["header"] = header;
        }
        string text = JsonSerializer.Serialize(data); Dictionary<string, string> fields;
        if (eapi)
        {
            string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("nobody" + path + "use" + text + "md5forencrypt"))).ToLowerInvariant();
            fields = new() { ["params"] = Convert.ToHexString(Encrypt(path + "-36cd479b6b5-" + text + "-36cd479b6b5-" + hash, "e82ckenh8dichen8", false)) };
        }
        else fields = Weapi(text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(18));
        try
        {
            string origin = eapi ? path == "/api/feedback/weblog" ? "https://clientlog.music.163.com/eapi/" : "https://interface.music.163.com/eapi/" : "https://music.163.com/weapi/";
            using var request = new HttpRequestMessage(HttpMethod.Post, origin + path[5..]) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Referrer = new Uri("https://music.163.com/");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new ApiException($"网易云连接失败（HTTP {(int)response.StatusCode}）。");
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream(); byte[] buffer = new byte[8192]; int n;
            while ((n = await body.ReadAsync(buffer, timeout.Token)) > 0) { if (memory.Length + n > 12 * 1024 * 1024) throw new ApiException("网易云返回内容过大。"); memory.Write(buffer, 0, n); }
            using var json = JsonDocument.Parse(memory.ToArray()); var root = json.RootElement;
            int code = root.TryGetProperty("code", out var value) && value.TryGetInt32(out int number) ? number : 200;
            if (code != 200 && !(allowLoginCodes && code is 800 or 801 or 802 or 803)) throw new ApiException(code is 301 or 302 ? "请在网易云账号窗口重新扫码登录。" : code is 460 or 503 ? "网易云暂时拒绝请求，请稍后再试。" : $"网易云操作未完成（{code}）。");
            return root.Clone();
        }
        catch (HttpRequestException) { throw new ApiException("无法连接网易云，请检查网络或代理。"); }
        catch (JsonException) { throw new ApiException("网易云响应格式发生变化，请稍后重试。"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException("网易云连接超时，请稍后重试。"); }
    }
    public async Task<string> CreateLoginKeyAsync(CancellationToken ct)
    {
        var root = await RequestAsync("/api/login/qrcode/unikey", new() { ["type"] = 3 }, ct);
        string key = NavidromeApiClient.Text(root, "unikey"); if (key.Length == 0 && root.TryGetProperty("data", out var data)) key = NavidromeApiClient.Text(data, "unikey");
        if (key.Length == 0) throw new ApiException("无法生成网易云登录二维码。"); return key;
    }
    public async Task<int> CheckLoginAsync(string key, CancellationToken ct)
    {
        var root = await RequestAsync("/api/login/qrcode/client/login", new() { ["key"] = key, ["type"] = 3 }, ct, allowLoginCodes: true);
        int code = root.GetProperty("code").GetInt32(); if (code == 803) { await RefreshAccountAsync(ct); SaveAccount(); AccountChanged?.Invoke(); } return code;
    }
    public async Task RefreshAccountAsync(CancellationToken ct = default)
    {
        var root = await RequestAsync("/api/w/nuser/account/get", ct: ct);
        if (!root.TryGetProperty("profile", out var profile) || profile.ValueKind != JsonValueKind.Object) throw new ApiException("网易云登录已失效，请重新扫码。");
        string user = NavidromeApiClient.Text(profile, "userId"); if (user != _login.UserId) { _liked = null; _addresses.Clear(); }
        _login = _login with { UserId = user, Nickname = NavidromeApiClient.Text(profile, "nickname") }; SaveAccount(); try { await LoadLikesAsync(ct); } catch (ApiException) { } AccountChanged?.Invoke();
    }
    private void SaveAccount()
    {
        string cookie = string.Join("; ", _cookies.GetCookies(new Uri("https://music.163.com")).Cast<Cookie>().Where(c => !c.Expired).Select(c => c.Name + "=" + c.Value));
        _login = _login with { ProtectedCookie = CredentialService.Protect(cookie) }; Store.Write("netease-account.json", _login);
    }
    public void RemoveAccount()
    {
        _addresses.Clear(); _liked = null;
        foreach (Cookie cookie in _cookies.GetCookies(new Uri("https://music.163.com"))) cookie.Expired = true;
        _login = new("", _login.DeviceId); AddCookie("os", "pc"); AddCookie("appver", "3.1.1"); AddCookie("deviceId", _login.DeviceId); Store.Write("netease-account.json", _login); AccountChanged?.Invoke();
    }
    public void Dispose() { _http.Dispose(); _media.Dispose(); }
}
