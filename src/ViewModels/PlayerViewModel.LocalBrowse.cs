using System.Security.Cryptography;
using System.Text;

namespace ChiliMusic;

public sealed partial class PlayerViewModel
{
    internal static string LocalAlbumId(Track t) => "local-album:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t.AlbumKey)));
    private IEnumerable<Track> LocalItemTracks(LibraryItem item) => item.Kind switch
    {
        "local-album" => _localTracks.Where(t => LocalAlbumId(t) == item.Id || item.Id.Length != 76 && t.Album == item.Title),
        "local-artist" => _localTracks.Where(t => t.ArtistNames.Contains(item.Title, StringComparer.CurrentCultureIgnoreCase)),
        "local-genre" => _localTracks.Where(t => t.Genre == item.Title),
        "local-year" => _localTracks.Where(t => t.Year.ToString() == item.Title),
        "local-folder" => _localTracks.Where(t => Path.GetDirectoryName(t.LocalPath) == item.Title),
        _ => []
    };
    private bool FetchLocalPage(List<Track> tracks, List<LibraryItem> items, out bool more)
    {
        more = false;
        if (Section == "本机历史")
        {
            var history = ListeningHistory.Where(h => h.Track.IsLocal || Api.CacheScopeAllowed(h.Scope)).Select(h => h.Track).DistinctBy(t => t.Id).ToList();
            tracks.AddRange(history.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < history.Count; return true;
        }
        bool detail = _browseItem?.Kind is "local-album" or "local-artist" or "local-genre" or "local-year" or "local-folder";
        bool local = IsLocalCatalog && Section is "最近添加" or "专辑" or "歌手" or "流派" or "年份" or "文件夹" or "最近播放" or "收藏" or "随机" || Section == "本地音乐" && LocalView != "歌曲";
        if (!detail && !local) return false;
        if (detail) { var all = LocalItemTracks(_browseItem!).OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList(); tracks.AddRange(_browseItem!.Kind == "local-album" ? all : all.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < all.Count; return true; }
        string view = Section == "本地音乐" ? LocalView : Section;
        if (view is "最近播放") { tracks.AddRange(ListeningHistory.Where(h => h.Track.IsLocal).Select(h => h.Track).DistinctBy(t => t.Id)); return true; }
        var source = _localTracks.AsEnumerable();
        if (view == "收藏") source = source.Where(t => t.Starred != null);
        if (view is "专辑" or "歌手" or "流派" or "年份" or "文件夹")
        {
            var groups = view switch
            {
                "专辑" => source.GroupBy(t => LocalAlbumId(t)),
                "歌手" => source.SelectMany(t => t.ArtistNames.Select(a => (Name: a, Track: t))).GroupBy(p => p.Name, p => p.Track),
                "流派" => source.GroupBy(t => string.IsNullOrWhiteSpace(t.Genre) ? "未知流派" : t.Genre),
                "年份" => source.GroupBy(t => t.Year.ToString()),
                _ => source.GroupBy(t => Path.GetDirectoryName(t.LocalPath) ?? "")
            };
            var all = groups.Select(g => new LibraryItem(view == "专辑" ? g.Key : "local:" + view + ":" + g.Key, view == "专辑" ? g.First().Album : g.Key, view == "专辑" ? $"{(g.First().AlbumArtist.Length > 0 ? g.First().AlbumArtist : g.First().Artist)} · {g.First().Year} · {g.Count()} 首" : $"{g.Count()} 首歌曲", view switch { "专辑" => "local-album", "歌手" => "local-artist", "流派" => "local-genre", "年份" => "local-year", _ => "local-folder" }) { LocalCoverPath = g.FirstOrDefault(t => t.EmbeddedCoverPath != null)?.EmbeddedCoverPath }).OrderBy(i => i.Title).ToList();
            items.AddRange(all.Skip(_albumOffset).Take(PageSize)); _albumOffset += items.Count; more = _albumOffset < all.Count; return true;
        }
        var songs = (view == "最近添加" ? source.OrderByDescending(t => t.FileModifiedUtc) : view == "随机" ? source.OrderBy(t => t.Id) : source.OrderBy(t => t.Album).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber)).ToList();
        tracks.AddRange(songs.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < songs.Count; return true;
    }
    private async Task<bool> FetchScopedSearchAsync(List<Track> tracks, List<LibraryItem> items, CancellationToken ct)
    {
        string scope = SearchScope; int offset = _songOffset; bool more = false;
        if (scope is "本地音乐" or "全部来源" || scope == "当前来源" && IsLocalCatalog)
        {
            var all = _localTracks.Where(t => Contains($"{t.Title} {t.Artist} {t.Album}", _browseQuery)).OrderBy(t => t.Title).ToList(); tracks.AddRange(all.Skip(offset).Take(PageSize)); more |= all.Count > offset + PageSize;
        }
        foreach (string source in new[] { "navidrome", "netease" })
        {
            if (!(scope == "全部来源" || scope == "Navidrome" && source == "navidrome" || scope == "网易云音乐" && source == "netease")) continue;
            if (source == "navidrome" && !Api.NavidromeConfigured) continue;
            try
            {
                var root = await Api.CallSourceAsync(source, "search3", ct, ("query", _browseQuery), ("songCount", PageSize.ToString()), ("songOffset", offset.ToString()), ("albumCount", "30"), ("albumOffset", _albumOffset.ToString()), ("artistCount", "30"), ("artistOffset", _artistOffset.ToString()));
                var songs = NavidromeApiClient.Tracks(root, "searchResult3"); tracks.AddRange(songs); more |= songs.Count == PageSize;
                if (root.TryGetProperty("searchResult3", out var search)) { int start = items.Count; Rows(search, "album", "album", items); Rows(search, "artist", "artist", items); more |= items.Skip(start).Count(i => i.Kind == "album") == 30 || items.Skip(start).Count(i => i.Kind == "artist") == 30; }
            }
            catch (ApiException) when (scope == "全部来源") { Store.Log("WARNING", "跨来源搜索的一个来源暂时不可用"); }
        }
        _songOffset += PageSize; _albumOffset += 30; _artistOffset += 30; return more;
    }
}
