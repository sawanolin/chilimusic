using System.Net.Http.Headers;

namespace ChiliMusic;

internal sealed partial class RemoteControlService
{
    private string? _mobileOwner;
    private async Task<LanResponse> AudioAsync(LanRequest request, Dictionary<string, string> query, CancellationToken ct)
    {
        if (!request.Headers.ContainsKey("X-Chili-Token") && request.Headers.TryGetValue("Cookie", out var cookie))
        {
            var part = cookie.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("ChiliRemote="));
            if (part != null) request.Headers["X-Chili-Token"] = part[12..];
        }
        if (!Authorized(request)) return Error("请先配对", 401);
        var track = await Ui(() => _tracks.GetValueOrDefault(query.GetValueOrDefault("key", "")));
        if (track == null) return Error("歌曲不存在，请刷新曲库", 404);
        string? range = request.Headers.GetValueOrDefault("Range");
        if (range != null && (!RangeHeaderValue.TryParse(range, out var parsed) || parsed.Unit != "bytes" || parsed.Ranges.Count != 1)) return Error("播放范围无效", 416);
        string? local = await Ui(() => track.IsLocal ? track.LocalPath : query.GetValueOrDefault("format") == "mp3" && track.Suffix?.ToLowerInvariant() != "mp3" ? null : _vm.Offline.GetPath(track));
        if (local != null)
        {
            FileStream input; try { input = new(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true); } catch (IOException) { return Error("音乐文件不可用", 404); }
            long start = 0, end = input.Length - 1, total = input.Length;
            if (range != null)
            {
                var item = RangeHeaderValue.Parse(range).Ranges.First(); start = item.From ?? Math.Max(0, total - (item.To ?? 0)); end = item.From == null ? total - 1 : Math.Min(item.To ?? total - 1, total - 1);
                if (start >= total || start > end) { input.Dispose(); return new(416, "text/plain", [], new() { ["Content-Range"] = "bytes */" + total }); }
            }
            input.Position = start; long length = Math.Max(0, end - start + 1); var headers = new Dictionary<string, string> { ["Accept-Ranges"] = "bytes" };
            if (range != null) headers["Content-Range"] = $"bytes {start}-{end}/{total}";
            return new(range == null ? 200 : 206, AudioType(track), [], headers, length, (output, token) => LanHttpServer.CopyAudioAsync(input, output, length, false, token), input);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response; try { response = await _vm.Api.OpenPhoneStreamAsync(track, query.GetValueOrDefault("format") == "mp3", range, timeout.Token); } catch (ApiException) { return Error("音频加载失败，请检查电脑端曲库连接", 400); }
        var body = await response.Content.ReadAsStreamAsync(ct); var extra = new Dictionary<string, string>();
        if (response.Content.Headers.ContentRange is { } contentRange) extra["Content-Range"] = contentRange.ToString();
        if (response.Headers.AcceptRanges.Contains("bytes")) extra["Accept-Ranges"] = "bytes";
        long? size = response.Content.Headers.ContentLength; string contentType = response.Content.Headers.ContentType?.MediaType ?? AudioType(track);
        return new((int)response.StatusCode, contentType, [], extra, size, (output, token) => LanHttpServer.CopyAudioAsync(body, output, size, size == null, token), response);
    }
    private static string AudioType(Track track) => track.Suffix?.ToLowerInvariant() switch { "mp3" => "audio/mpeg", "m4a" or "aac" => "audio/mp4", "flac" => "audio/flac", "wav" => "audio/wav", "ogg" or "opus" => "audio/ogg", _ => "application/octet-stream" };
}
