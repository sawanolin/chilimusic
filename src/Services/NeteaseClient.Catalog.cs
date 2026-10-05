using System.Text.Json;

namespace ChiliMusic;

public sealed partial class NeteaseClient
{
    private sealed record RecentEntry(string UserId, DateTime PlayedUtc, Track Track);
    private readonly List<RecentEntry> _recent = Store.Read("netease-recent.json", new List<RecentEntry>()).Where(r => r.Track != null && !string.IsNullOrEmpty(r.UserId)).ToList();
    private HashSet<string>? _liked;
    private JsonElement[]? _newAlbums;
    private DateTime _albumsLoaded;
    public bool? IsLiked(string id) => _liked?.Contains(id);
    private async Task LoadLikesAsync(CancellationToken ct)
    {
        if (_liked != null || !LoggedIn || UserId.Length == 0) return;
        var root = await RequestAsync("/api/song/like/get", new() { ["uid"] = UserId }, ct); _liked = Rows(root, "ids").Select(id => "ncm:" + id).ToHashSet();
    }
    public static string RawId(string id) { string raw = id.StartsWith("ncm:", StringComparison.Ordinal) ? id[4..] : id; if (!long.TryParse(raw, out long number) || number <= 0) throw new ApiException("网易云歌曲编号无效。"); return raw; }
    private static string Text(JsonElement row, string key, string fallback = "") => NavidromeApiClient.Text(row, key, fallback);
    private static double Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.TryGetDouble(out double n) ? n : 0;
    private static IEnumerable<JsonElement> Rows(JsonElement root, string key) => root.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    private Track Song(JsonElement row)
    {
        var album = row.TryGetProperty("al", out var al) ? al : row.TryGetProperty("album", out al) ? al : default;
        string id = "ncm:" + Text(row, "id");
        return new() { Id = id, Title = Text(row, "name"), Artist = string.Join(" / ", Rows(row, row.TryGetProperty("ar", out _) ? "ar" : "artists").Select(a => Text(a, "name"))), AlbumId = album.ValueKind == JsonValueKind.Object ? "ncm:" + Text(album, "id") : "", Album = album.ValueKind == JsonValueKind.Object ? Text(album, "name") : "", CoverArt = album.ValueKind == JsonValueKind.Object && Text(album, "picUrl").Length > 0 ? "ncm-cover:" + Text(album, "picUrl") : null, Duration = (Number(row, "dt") > 0 ? Number(row, "dt") : Number(row, "duration")) / 1000, Suffix = "mp3", TrackNumber = (uint)Math.Max(0, Number(row, "no")), Starred = _liked?.Contains(id) == true ? "liked" : null };
    }
    private static object Item(JsonElement row, string kind) => new { id = "ncm:" + Text(row, "id"), name = Text(row, "name"), artist = row.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object ? Text(artist, "name") : kind == "playlist" ? "网易云歌单" : "", coverArt = Text(row, "picUrl", Text(row, "coverImgUrl")).Length > 0 ? "ncm-cover:" + Text(row, "picUrl", Text(row, "coverImgUrl")) : null };
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private async Task EnsureAccountAsync(CancellationToken ct) { if (!LoggedIn) throw new ApiException("请先扫码登录网易云音乐。"); if (UserId.Length == 0) await RefreshAccountAsync(ct); }
    private async Task<List<Track>> DetailsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var result = new List<Track>(); var list = ids.Select(RawId).Distinct().Take(10000).ToArray();
        foreach (var chunk in list.Chunk(200)) { var root = await RequestAsync("/api/v3/song/detail", new() { ["c"] = JsonSerializer.Serialize(chunk.Select(id => new { id = long.Parse(id) })) }, ct); result.AddRange(Rows(root, "songs").Select(Song)); }
        return result;
    }
    private async Task<List<Track>> FavoritesAsync(CancellationToken ct)
    {
        await EnsureAccountAsync(ct); var root = await RequestAsync("/api/song/like/get", new() { ["uid"] = UserId }, ct);
        var ids = Rows(root, "ids").Select(x => x.ToString()).ToList(); _liked = ids.Select(id => "ncm:" + id).ToHashSet(); return await DetailsAsync(ids, ct);
    }
    private async Task<List<Track>> PlaylistAsync(string id, int offset, int limit, CancellationToken ct)
    {
        var root = await RequestAsync("/api/v6/playlist/detail", new() { ["id"] = RawId(id), ["n"] = 100000, ["s"] = 0 }, ct);
        var playlist = root.GetProperty("playlist"); var ids = Rows(playlist, "trackIds").Skip(offset).Take(limit).Select(t => Text(t, "id")).ToArray(); return await DetailsAsync(ids, ct);
    }
    public async Task<List<LyricsDocument>> LyricsAsync(string id, CancellationToken ct)
    {
        var root = await RequestAsync("/api/song/lyric", new() { ["id"] = RawId(id), ["lv"] = -1, ["tv"] = -1, ["rv"] = -1, ["kv"] = -1 }, ct); var documents = new List<LyricsDocument>();
        foreach (var pair in new[] { ("lrc", "歌词"), ("tlyric", "翻译"), ("romalrc", "罗马音") }) if (root.TryGetProperty(pair.Item1, out var body) && Text(body, "lyric").Length > 0) { var parsed = LyricsService.Parse(Text(body, "lyric")); documents.Add(new(pair.Item2, parsed.Lines)); }
        return documents;
    }
    public void RecordRecent(Track track) { if (!LoggedIn) return; _recent.RemoveAll(t => t.UserId == UserId && t.Track.Id == track.Id); _recent.Insert(0, new(UserId, DateTime.UtcNow, track)); if (_recent.Count > 600) _recent.RemoveRange(600, _recent.Count - 600); Store.Write("netease-recent.json", _recent); }
    public async Task<List<Track>> RecentSongsAsync(CancellationToken ct)
    {
        await EnsureAccountAsync(ct);
        var root = await RequestAsync("/api/play-record/song/list", new() { ["limit"] = 300 }, ct);
        var cloud = Rows(root.GetProperty("data"), "list").Where(row => row.TryGetProperty("data", out var song) && song.ValueKind == JsonValueKind.Object).Select(row => new RecentEntry(UserId, DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Clamp(Number(row, "playTime"), 0, 253402300799999)).UtcDateTime, Song(row.GetProperty("data"))));
        return cloud.Concat(_recent.Where(r => r.UserId == UserId && r.Track != null)).OrderByDescending(r => r.PlayedUtc).DistinctBy(r => r.Track.Id).Take(300).Select(r => r.Track).ToList();
    }
    public async Task<JsonElement> SubsonicAsync(string endpoint, (string Key, string Value)[] args, CancellationToken ct)
    {
        string Arg(string name, string fallback = "") => args.LastOrDefault(p => p.Key == name).Value ?? fallback;
        int Int(string name, int fallback) => int.TryParse(Arg(name), out var n) ? Math.Clamp(n, 0, 100000) : fallback;
        switch (endpoint)
        {
            case "ping": return Json(new { status = "ok", type = "网易云音乐", version = "" });
            case "search3":
                var songs = new List<Track>(); var albums = new List<object>(); var artists = new List<object>();
                foreach (var (kind, countKey, offsetKey) in new[] { (1, "songCount", "songOffset"), (10, "albumCount", "albumOffset"), (100, "artistCount", "artistOffset") })
                {
                    int count = Int(countKey, 30); if (count == 0) continue;
                    var root = await RequestAsync("/api/cloudsearch/pc", new() { ["s"] = Arg("query"), ["type"] = kind, ["limit"] = Math.Min(100, count), ["offset"] = Int(offsetKey, 0), ["total"] = true }, ct);
                    if (!root.TryGetProperty("result", out var found)) continue;
                    if (kind == 1) songs.AddRange(Rows(found, "songs").Select(Song)); else foreach (var row in Rows(found, kind == 10 ? "albums" : "artists")) (kind == 10 ? albums : artists).Add(Item(row, kind == 10 ? "album" : "artist"));
                }
                return Json(new { searchResult3 = new { song = songs, album = albums, artist = artists } });
            case "getAlbum":
                var albumRoot = await RequestAsync("/api/v1/album/" + RawId(Arg("id")), ct: ct); return Json(new { album = new { song = Rows(albumRoot, "songs").Select(Song).ToArray() } });
            case "getPlaylist": return Json(new { playlist = new { entry = await PlaylistAsync(Arg("id"), Int("offset", 0), Int("size", 10000), ct) } });
            case "getPlaylists":
                await EnsureAccountAsync(ct); var lists = new List<object>();
                for (int offset = 0; offset < 2000; offset += 100) { var root = await RequestAsync("/api/user/playlist", new() { ["uid"] = UserId, ["limit"] = 100, ["offset"] = offset, ["includeVideo"] = false }, ct); var page = Rows(root, "playlist").ToArray(); lists.AddRange(page.Select(row => Item(row, "playlist"))); if (page.Length < 100) break; }
                return Json(new { playlists = new { playlist = lists } });
            case "getStarred2": return Json(new { starred2 = new { song = await FavoritesAsync(ct) } });
            case "star": case "unstar":
                await EnsureAccountAsync(ct); await RequestAsync("/api/radio/like", new() { ["trackId"] = RawId(Arg("id")), ["like"] = endpoint == "star", ["alg"] = "itembased", ["time"] = 3 }, ct); if (endpoint == "star") _liked?.Add(Arg("id")); else _liked?.Remove(Arg("id")); return Json(new { status = "ok" });
            case "getDailyRecommendations":
                await EnsureAccountAsync(ct); var daily = await RequestAsync("/api/v3/discovery/recommend/songs", new(), ct); return Json(new { randomSongs = new { song = Rows(daily.GetProperty("data"), "dailySongs").Select(Song).ToArray() } });
            case "getRecentSongs": return Json(new { recentSongs = new { song = await RecentSongsAsync(ct) } });
            case "getRandomSongs":
                var choices = await RequestAsync("/api/personalized/playlist", new() { ["limit"] = 12, ["total"] = true, ["n"] = 1000 }, ct); var rows = Rows(choices, "result").ToArray(); if (rows.Length == 0) return Json(new { randomSongs = new { song = Array.Empty<Track>() } });
                var randomSongs = await PlaylistAsync("ncm:" + Text(rows[Random.Shared.Next(rows.Length)], "id"), 0, 100, ct); Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(randomSongs)); return Json(new { randomSongs = new { song = randomSongs } });
            case "getAlbumList2":
                if (Arg("type") == "recent") return Json(new { albumList2 = new { album = (await RecentSongsAsync(ct)).GroupBy(t => t.AlbumId).Where(g => g.Key.Length > 4).Skip(Int("offset", 0)).Take(Int("size", 60)).Select(g => new { id = g.Key, name = g.First().Album, artist = g.First().Artist, coverArt = g.First().CoverArt }).ToArray() } });
                if (_newAlbums == null || DateTime.UtcNow - _albumsLoaded > TimeSpan.FromMinutes(5))
                {
                    var fresh = await RequestAsync("/api/discovery/new/albums/area", new() { ["area"] = "ALL", ["limit"] = 100, ["offset"] = 0, ["type"] = "new", ["year"] = DateTime.Now.Year, ["month"] = DateTime.Now.Month, ["total"] = false, ["rcmd"] = true }, ct);
                    _newAlbums = Rows(fresh, "monthData").Concat(Rows(fresh, "weekData")).DistinctBy(row => Text(row, "id")).ToArray(); _albumsLoaded = DateTime.UtcNow;
                }
                return Json(new { albumList2 = new { album = _newAlbums.Skip(Int("offset", 0)).Take(Math.Min(100, Int("size", 60))).Select(row => Item(row, "album")).ToArray() } });
            case "getArtists":
                var top = await RequestAsync("/api/artist/top", new() { ["limit"] = 100, ["offset"] = 0, ["total"] = true }, ct); return Json(new { artists = new { index = new[] { new { artist = Rows(top, "artists").Select(row => Item(row, "artist")).ToArray() } } } });
            case "getArtist":
                var hot = await RequestAsync("/api/artist/albums/" + RawId(Arg("id")), new() { ["limit"] = 100, ["offset"] = 0, ["total"] = true }, ct); return Json(new { artist = new { album = Rows(hot, "hotAlbums").Select(row => Item(row, "album")).ToArray() } });
            case "scrobble":
                var recentTrack = _recent.FirstOrDefault(r => r.UserId == UserId && r.Track?.Id == Arg("id"))?.Track; string sourceId = recentTrack?.AlbumId.StartsWith("ncm:") == true ? RawId(recentTrack.AlbumId) : "0";
                if (LoggedIn && Arg("submission") != "true") await RequestAsync("/api/feedback/weblog", new() { ["logs"] = JsonSerializer.Serialize(new[] { new { action = "startplay", json = new { id = RawId(Arg("id")), type = "song", mainsite = "1", mainsiteWeb = "1", content = "id=" + sourceId } } }) }, ct, eapi: true);
                return Json(new { status = "ok" });
            case "createPlaylist":
                await EnsureAccountAsync(ct); var initial = args.Where(p => p.Key == "songId").Select(p => long.Parse(RawId(p.Value))).ToArray();
                var made = await RequestAsync("/api/playlist/create", new() { ["name"] = Arg("name"), ["privacy"] = 0, ["type"] = "NORMAL" }, ct); string madeId = Text(made, "id");
                if (initial.Length > 0) await RequestAsync("/api/playlist/manipulate/tracks", new() { ["op"] = "add", ["pid"] = madeId, ["trackIds"] = JsonSerializer.Serialize(initial), ["imme"] = true }, ct);
                return Json(new { playlist = new { id = "ncm:" + madeId } });
            case "updatePlaylist":
                await EnsureAccountAsync(ct); string playlistId = RawId(Arg("playlistId"));
                if (Arg("name").Length > 0) await RequestAsync("/api/playlist/update/name", new() { ["id"] = playlistId, ["name"] = Arg("name") }, ct);
                var additions = args.Where(p => p.Key == "songIdToAdd").Select(p => RawId(p.Value)).ToArray();
                var removals = args.Where(p => p.Key == "songIndexToRemove").Select(p => int.Parse(p.Value)).ToArray();
                if (additions.Length > 0) await RequestAsync("/api/playlist/manipulate/tracks", new() { ["op"] = "add", ["pid"] = playlistId, ["trackIds"] = JsonSerializer.Serialize(additions.Select(long.Parse)), ["imme"] = true }, ct);
                if (removals.Length > 0) { var full = await PlaylistAsync("ncm:" + playlistId, 0, 10000, ct); if (removals.Any(i => i < 0 || i >= full.Count)) throw new ApiException("歌单已变化，请刷新后重试。"); await RequestAsync("/api/playlist/manipulate/tracks", new() { ["op"] = "del", ["pid"] = playlistId, ["trackIds"] = JsonSerializer.Serialize(removals.Select(i => long.Parse(RawId(full[i].Id)))), ["imme"] = true }, ct); }
                return Json(new { status = "ok" });
            default: throw new ApiException("网易云暂不支持这项操作。");
        }
    }
}
