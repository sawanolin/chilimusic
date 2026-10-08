using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace ChiliMusic;

public sealed class LyricLine(double? time, string text, string translation = "") : INotifyPropertyChanged
{
    public double? Time { get; } = time;
    public string Text { get; } = text;
    public string Translation { get; } = translation;
    private bool _active;
    public bool IsActive { get => _active; set { if (_active == value) return; _active = value; PropertyChanged?.Invoke(this, new(nameof(IsActive))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record LyricsDocument(string Language, List<LyricLine> Lines, string Source = "") { public override string ToString() => Language; }
public sealed class LyricsService(NavidromeApiClient api)
{
    public string CachePath(Track track) => Path.Combine(Store.Root, "cache", "lyrics", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(api.ScopeForId(track.Id) + "\0" + track.Id))) + ".lrc");
    public async Task<List<LyricsDocument>> ApplyMatchAsync(Track original, Track match, CancellationToken ct)
    {
        if (api is not MusicApiClient music) throw new ApiException("当前无法匹配网易云歌词。");
        var documents = await music.Netease.LyricsAsync(match.Id, ct); if (!documents.Any(d => d.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text)))) throw new ApiException("这首歌曲没有可用歌词，请选择其他版本。");
        documents = documents.Select(d => d with { Source = "网易云 · " + match.Artist + " · " + match.Title }).ToList(); Store.Write(CachePath(original) + ".match.json", documents); return documents;
    }
    public async Task<List<LyricsDocument>> LoadAsync(Track track, CancellationToken ct)
    {
        string custom = CachePath(track);
        if (File.Exists(custom + ".match.json")) { var manual = Store.Read(custom + ".match.json", new List<LyricsDocument>()); if (manual.Count > 0) return manual; }
        if (File.Exists(custom)) return [Parse(await ReadTextAsync(custom, ct))];
        string cached = custom + ".json";
        if (File.Exists(cached)) { var saved = await Task.Run(() => Store.Read(cached, new List<LyricsDocument>()), ct); if (saved.Count > 0) return saved; }
        if (track.IsLocal)
        {
            var lrc = Path.ChangeExtension(track.LocalPath!, ".lrc");
            if (File.Exists(lrc)) return [Parse(await ReadTextAsync(lrc, ct))];
            if (!string.IsNullOrWhiteSpace(track.EmbeddedLyrics)) return [Parse(track.EmbeddedLyrics)];
            return await SupplementAsync(track, cached, ct);
        }
        if (track.IsNetease && api is MusicApiClient music) { var documents = await music.Netease.LyricsAsync(track.Id, ct); if (documents.Count > 0) Store.Write(cached, documents); return documents; }
        if (api is MusicApiClient disconnected && !disconnected.NavidromeConfigured) return await SupplementAsync(track, cached, ct);
        try
        {
            var root = api is MusicApiClient sources ? await sources.CallSourceAsync("server", "getLyricsBySongId", ct, ("id", track.Id)) : await api.CallAsync("getLyricsBySongId", ct, ("id", track.Id)); var documents = new List<LyricsDocument>();
            if (root.TryGetProperty("lyricsList", out var list) && list.TryGetProperty("structuredLyrics", out var lyrics))
                foreach (var item in lyrics.EnumerateArray())
                {
                    var lines = new List<LyricLine>(); bool synced = item.TryGetProperty("synced", out var s) && s.GetBoolean(); double offset = item.TryGetProperty("offset", out var off) && off.TryGetDouble(out var o) ? o / 1000 : 0;
                    if (item.TryGetProperty("line", out var entries)) foreach (var line in entries.EnumerateArray()) lines.Add(new(synced && line.TryGetProperty("start", out var start) && start.TryGetDouble(out var ms) ? ms / 1000 + offset : null, NavidromeApiClient.Text(line, "value")));
                    if (lines.Count > 0) documents.Add(new(NavidromeApiClient.Text(item, "lang", "歌词"), lines.OrderBy(l => l.Time ?? 0).ToList()));
                }
            if (documents.Count > 0) { Store.Write(cached, documents); return documents; }
        }
        catch (ApiException) { }
        try { var root = api is MusicApiClient catalogs ? await catalogs.CallSourceAsync("server", "getLyrics", ct, ("artist", track.Artist), ("title", track.Title)) : await api.CallAsync("getLyrics", ct, ("artist", track.Artist), ("title", track.Title)); if (root.TryGetProperty("lyrics", out var lyric)) { string value = NavidromeApiClient.Text(lyric, "value"); if (value.Length > 0) { List<LyricsDocument> result = [Parse(value)]; Store.Write(cached, result); return result; } } } catch (ApiException) { }
        return await SupplementAsync(track, cached, ct);
    }
    private async Task<List<LyricsDocument>> SupplementAsync(Track track, string cached, CancellationToken ct)
    {
        if (api is not MusicApiClient music || !music.Settings.NeteaseLyrics) return [];
        try
        {
            var match = await music.Netease.MatchLyricsAsync(track, ct); if (match == null) return [];
            var docs = (await music.Netease.LyricsAsync(match.Id, ct)).Select(d => d with { Source = "网易云 · " + match.Artist + " · " + match.Title }).ToList();
            if (docs.Any(d => d.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Text)))) { Store.Write(cached, docs); return docs; }
        }
        catch (ApiException) { }
        return [];
    }
    public static LyricsDocument Parse(string text)
    {
        var lines = new List<LyricLine>(); var plain = new List<LyricLine>(); double offset = 0;
        var offsetMatch = Regex.Match(text, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase); if (offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, out var o)) offset = o / 1000;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var times = Regex.Matches(raw, @"\[(\d{1,3}):(\d{1,2}(?:[\.,]\d+)?)\]"); string value = Regex.Replace(raw, @"\[[^\]]*\]", "").Trim();
            if (times.Count > 0) foreach (Match time in times) { if (double.TryParse(time.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) lines.Add(new(int.Parse(time.Groups[1].Value) * 60 + seconds + offset, value)); }
            else if (value.Length > 0) plain.Add(new(null, value));
        }
        return new("歌词", lines.Count > 0 ? lines.OrderBy(l => l.Time).ToList() : plain);
    }
    public static async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path); if (info.Length > 1024 * 1024) throw new ApiException("歌词文件超过 1 MB。");
        var bytes = await File.ReadAllBytesAsync(path, ct); try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936).GetString(bytes); }
    }
}
