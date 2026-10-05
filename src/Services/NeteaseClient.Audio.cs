using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace ChiliMusic;

public sealed partial class NeteaseClient
{
    private sealed record PlayAddress(string Url, DateTime Expires, string Type, int BitRate, bool Preview);
    private readonly ConcurrentDictionary<string, PlayAddress> _addresses = new();
    private readonly HttpClient _media = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10), AutomaticDecompression = System.Net.DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    internal static Uri MediaUri(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || !(uri.Host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".music.163.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".music.163.net", StringComparison.OrdinalIgnoreCase))) throw new ApiException("网易云返回了无效的音频或封面地址。");
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
    }
    public async Task<string> ResolveUrlAsync(Track track, string quality, bool mp3, CancellationToken ct)
    {
        string id = RawId(track.Id); string level = mp3 ? "exhigh" : quality is "standard" or "higher" or "exhigh" or "lossless" or "hires" ? quality : "exhigh";
        string key = UserId + ":" + id + ":" + level;
        if (!_addresses.TryGetValue(key, out var address) || address.Expires <= DateTime.UtcNow)
        {
            var root = await RequestAsync("/api/song/enhance/player/url/v1", new() { ["ids"] = "[" + id + "]", ["level"] = level, ["encodeType"] = mp3 || level is not ("lossless" or "hires") ? "mp3" : "flac" }, ct, eapi: true);
            var rows = Rows(root, "data").ToArray(); if (rows.Length == 0) throw new ApiException("网易云没有提供这首歌的播放地址。"); var row = rows[0]; string url = Text(row, "url");
            if (string.IsNullOrWhiteSpace(url) || url == "null") throw new ApiException("这首网易云歌曲暂不可播放，请检查账号权益或版权状态。");
            var uri = MediaUri(url); bool preview = row.TryGetProperty("freeTrialInfo", out var info) && info.ValueKind == System.Text.Json.JsonValueKind.Object;
            address = new(uri.AbsoluteUri, DateTime.UtcNow.AddSeconds(Math.Clamp(Number(row, "expi") - 60, 20, 900)), Text(row, "type", "mp3"), (int)(Number(row, "br") / 1000), preview);
            if (_addresses.Count >= 500) _addresses.Clear(); _addresses[key] = address;
        }
        track.Suffix = address.Type; track.ContentType = address.Type == "flac" ? "audio/flac" : "audio/mpeg"; track.BitRate = address.BitRate; track.Preview = address.Preview; track.NotifyMetadata();
        return address.Url;
    }
    private async Task<HttpResponseMessage> GetMediaAsync(Uri uri, string? range, CancellationToken ct)
    {
        for (int redirect = 0; redirect <= 4; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("chilimusic/1.3");
            if (range != null && RangeHeaderValue.TryParse(range, out var parsed)) request.Headers.Range = parsed;
            var response = await _media.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var target = response.Headers.Location; response.Dispose(); if (target == null) throw new ApiException("网易云音频重定向失败。"); uri = MediaUri(new Uri(uri, target).AbsoluteUri); continue;
            }
            if (!response.IsSuccessStatusCode) { int code = (int)response.StatusCode; response.Dispose(); throw new ApiException($"网易云音频读取失败（HTTP {code}）。"); }
            return response;
        }
        throw new ApiException("网易云音频重定向次数过多。");
    }
    public async Task<HttpResponseMessage> OpenAudioAsync(Track track, string quality, bool mp3, string? range, CancellationToken ct)
    {
        var url = await ResolveUrlAsync(track, quality, mp3, ct); var response = await GetMediaAsync(MediaUri(url), range, ct);
        string type = response.Content.Headers.ContentType?.MediaType ?? ""; if (type.StartsWith("text/") || type.Contains("json") || type.Contains("xml")) { response.Dispose(); throw new ApiException("网易云没有返回可播放音频。"); } return response;
    }
    public async Task<byte[]> CoverAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await GetMediaAsync(MediaUri(url), null, timeout.Token); await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream(); byte[] buffer = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(buffer, timeout.Token)) > 0) { if (memory.Length + n > 10 * 1024 * 1024) throw new ApiException("网易云封面超过 10 MB。"); memory.Write(buffer, 0, n); } return memory.ToArray();
    }
}
