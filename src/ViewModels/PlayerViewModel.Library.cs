using System.Windows.Data;
namespace ChiliMusic;

public sealed partial class PlayerViewModel
{
    public LocalLibraryService LocalLibrary { get; }
    public OfflineCacheService Offline { get; }
    public PlaylistService Playlists { get; }
    public event Action? SettingsChanged;
    private LibraryItem? _browseItem;
    private string _browseQuery = "";
    private (string Section, LibraryItem? Item, string Format, string Artist, string Album, string Category)? _beforeSearch;
    private readonly Stack<(string Section, string Query, LibraryItem? Item, string Format, string Artist, string Album, string Category)> _browseHistory = new();
    public bool CanGoBack => _browseHistory.Count > 0;
    public string BrowseQuery => _browseQuery;
    public bool CanSortAlbums => _browseItem == null && Section is "最近添加" or "专辑";
    public bool IsSearch => Section == "搜索";
    private int _songOffset, _albumOffset, _artistOffset;
    private bool _hasMore, _browsing;
    private const int PageSize = 60;
    private string _formatFilter = "全部格式", _artistFilter = "", _albumFilter = "";
    private string _searchCategory = "全部";
    private int _coverViewportGeneration;
    public bool HasMore { get => _hasMore; private set { _hasMore = value; Changed(); } }
    public bool IsBrowsing { get => _browsing; private set { _browsing = value; Changed(); } }
    public string FormatFilter { get => _formatFilter; set { if (_formatFilter == value) return; _formatFilter = value; ApplyResultFilter(); Changed(); } }
    public string ArtistFilter { get => _artistFilter; set { if (_artistFilter == value) return; _artistFilter = value; ApplyResultFilter(); Changed(); } }
    public string AlbumFilter { get => _albumFilter; set { if (_albumFilter == value) return; _albumFilter = value; ApplyResultFilter(); Changed(); } }
    public string SearchCategory { get => _searchCategory; set { if (_searchCategory == value) return; _searchCategory = value; ApplyResultFilter(); Changed(); } }
    public bool ShowSongs => Section != "搜索" || SearchCategory is "全部" or "歌曲";
    public bool ShowAlbums => SearchCategory != "歌曲";
    public string AlbumSort { get => Settings.AlbumSort; set { if (Settings.AlbumSort == value) return; Settings.AlbumSort = value; SaveSettings(); if (Navigation is "专辑" or "最近添加") Run(() => BrowseAsync("专辑")); } }
    public IReadOnlyList<OfflineEntry> OfflineEntries => Offline.Entries;
    public string OfflineSize => $"{Offline.Bytes / 1048576d:0.0} MB / {Settings.OfflineCacheMb} MB";
    public bool IsDownloading => Offline.Downloading;
    private string _downloadStatus = "选择歌曲或专辑后下载";
    public string DownloadStatus { get => _downloadStatus; set { _downloadStatus = value; Changed(); } }
    public async Task BrowseAsync(string section, string query = "", LibraryItem? item = null, bool remember = true)
    {
        if (remember && (section != Section || item?.Id != _browseItem?.Id || section != "搜索" && query != _browseQuery))
        {
            _browseHistory.Push((Section, _browseQuery, _browseItem, FormatFilter, ArtistFilter, AlbumFilter, SearchCategory));
            Changed(nameof(CanGoBack));
        }
        if (remember && section == "搜索" && Section != "搜索") _beforeSearch = (Section, _browseItem, FormatFilter, ArtistFilter, AlbumFilter, SearchCategory);
        _browseCts.Cancel(); _browseCts.Dispose(); _browseCts = new(); int generation = ++_browseGeneration;
        _browseItem = item; _browseQuery = query; _songOffset = _albumOffset = _artistOffset = 0;
        if (item == null && section != "搜索") Navigation = section;
        if (section != "搜索") { _searchCategory = "全部"; _formatFilter = "全部格式"; _artistFilter = _albumFilter = ""; Changed(nameof(SearchCategory)); Changed(nameof(FormatFilter)); Changed(nameof(ArtistFilter)); Changed(nameof(AlbumFilter)); }
        Section = section; Changed(nameof(BrowseQuery)); Changed(nameof(CanSortAlbums)); Changed(nameof(IsSearch)); Results.Clear(); Library.Clear(); HasMore = false; IsBrowsing = true; Status = "正在加载…";
        try { await FetchPageAsync(false, generation, _browseCts.Token); }
        finally { if (generation == _browseGeneration) IsBrowsing = false; }
    }
    public async Task LoadMoreAsync()
    {
        if (!HasMore || IsBrowsing) return; IsBrowsing = true; int generation = _browseGeneration;
        try { await FetchPageAsync(true, generation, _browseCts.Token); }
        finally { if (generation == _browseGeneration) IsBrowsing = false; }
    }
    private async Task FetchPageAsync(bool append, int generation, CancellationToken ct)
    {
        List<Track> tracks = []; List<LibraryItem> items = []; bool more = false;
        if (_browseItem?.Kind is "local-album" or "local-artist")
        {
            var all = _localTracks.Where(t => _browseItem.Kind == "local-album" ? t.Album == _browseItem.Title : t.Artist == _browseItem.Title).OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList(); tracks.AddRange(_browseItem.Kind == "local-album" ? all : all.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < all.Count;
        }
        else if (Navigation == "本地音乐" && Section == "搜索" || Section == "本地音乐" || Section == "搜索" && !Api.Configured)
        {
            var source = _localTracks.Where(t => Section != "搜索" || Contains($"{t.Title} {t.Artist} {t.Album}", _browseQuery)).OrderBy(t => t.Album).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList();
            tracks.AddRange(source.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < source.Count;
            if (Section == "搜索")
            {
                var albums = _localTracks.Where(t => Contains(t.Album, _browseQuery)).GroupBy(t => t.Album).Select(g => new LibraryItem("local-album:" + g.Key, g.Key, string.Join(" / ", g.Select(t => t.Artist).Distinct()), "local-album") { LocalCoverPath = g.FirstOrDefault(t => t.EmbeddedCoverPath != null)?.EmbeddedCoverPath }).ToList();
                var artists = _localTracks.Where(t => Contains(t.Artist, _browseQuery)).GroupBy(t => t.Artist).Select(g => new LibraryItem("local-artist:" + g.Key, g.Key, $"{g.Count()} 首歌曲", "local-artist")).ToList();
                items.AddRange(albums.Skip(_albumOffset).Take(30)); items.AddRange(artists.Skip(_artistOffset).Take(30)); _albumOffset += Math.Min(30, Math.Max(0, albums.Count - _albumOffset)); _artistOffset += Math.Min(30, Math.Max(0, artists.Count - _artistOffset)); more |= _albumOffset < albums.Count || _artistOffset < artists.Count;
            }
        }
        else if (Section == "离线音乐")
        {
            var all = Offline.Entries.Select(e => e.Track).ToList(); tracks.AddRange(all.Skip(_songOffset).Take(PageSize)); _songOffset += tracks.Count; more = _songOffset < all.Count;
        }
        else if (Section == "歌单" && _browseItem == null)
        {
            items.AddRange(Playlists.Local.Select(p => new LibraryItem(p.Id, p.Name, $"{p.Tracks.Count} 首歌曲 · 本地歌单", "local-playlist")));
            if (Api.Configured) { var root = await Api.CallAsync("getPlaylists", ct); if (root.TryGetProperty("playlists", out var lists)) Rows(lists, "playlist", "playlist", items); }
        }
        else if (_browseItem?.Kind == "local-playlist") tracks.AddRange(Playlists.Local.FirstOrDefault(p => p.Id == _browseItem.Id)?.Tracks ?? []);
        else if (Section == "最近播放" && IsNeteaseCatalog) tracks.AddRange(await Api.Netease.RecentSongsAsync(ct));
        else if (Section == "每日推荐") tracks.AddRange(NavidromeApiClient.Tracks(await Api.CallAsync("getDailyRecommendations", ct), "randomSongs"));
        else if (Section == "收藏") tracks.AddRange((Api.Configured ? await Api.StarredAsync() : []).Concat(_localTracks.Where(t => t.Starred != null)));
        else if (Section == "随机") tracks.AddRange(Api.Configured ? await Api.RandomAsync() : _localTracks.OrderBy(_ => Random.Shared.Next()));
        else if (Api.Configured)
        {
            string endpoint = Section == "搜索" ? "search3" : Section == "歌手" ? "getArtists" : "getAlbumList2";
            var args = new List<(string Key, string Value)>();
            if (_browseItem != null) { endpoint = _browseItem.Kind switch { "artist" => "getArtist", "playlist" => "getPlaylist", _ => "getAlbum" }; args.Add(("id", _browseItem.Id)); }
            else if (endpoint == "search3") { args.Add(("query", _browseQuery)); args.Add(("songCount", PageSize.ToString())); args.Add(("albumCount", "30")); args.Add(("artistCount", "30")); args.Add(("songOffset", _songOffset.ToString())); args.Add(("albumOffset", _albumOffset.ToString())); args.Add(("artistOffset", _artistOffset.ToString())); }
            else if (endpoint == "getAlbumList2") { args.Add(("type", Section == "最近播放" ? "recent" : Section == "最近添加" ? "newest" : Settings.AlbumSort)); args.Add(("size", PageSize.ToString())); args.Add(("offset", _albumOffset.ToString())); }
            var root = await Api.CallAsync(endpoint, ct, args.ToArray());
            if (ct.IsCancellationRequested || generation != _browseGeneration) return;
            if (endpoint == "search3" && root.TryGetProperty("searchResult3", out var search))
            {
                tracks.AddRange(NavidromeApiClient.Tracks(root, "searchResult3")); Rows(search, "album", "album", items); Rows(search, "artist", "artist", items);
                int albumCount = items.Count(i => i.Kind == "album"), artistCount = items.Count(i => i.Kind == "artist"); _songOffset += tracks.Count; _albumOffset += albumCount; _artistOffset += artistCount;
                more = tracks.Count == PageSize || albumCount == 30 || artistCount == 30;
            }
            if (endpoint == "getAlbum") tracks.AddRange(NavidromeApiClient.Tracks(root, "album").OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber));
            if (endpoint == "getPlaylist") tracks.AddRange(NavidromeApiClient.Tracks(root, "playlist", "entry"));
            if (endpoint == "getArtist" && root.TryGetProperty("artist", out var artist)) Rows(artist, "album", "album", items);
            if (endpoint == "getAlbumList2" && root.TryGetProperty("albumList2", out var albums)) { Rows(albums, "album", "album", items); _albumOffset += items.Count; more = items.Count == PageSize; }
            if (endpoint == "getArtists" && root.TryGetProperty("artists", out var artists) && artists.TryGetProperty("index", out var indexes)) foreach (var group in indexes.EnumerateArray()) Rows(group, "artist", "artist", items);
        }
        if (ct.IsCancellationRequested || generation != _browseGeneration) return;
        var songIds = Results.Select(t => t.Id).ToHashSet(); foreach (var track in tracks) if (songIds.Add(track.Id)) Results.Add(track);
        var itemIds = Library.Select(i => i.Kind + ":" + i.Id).ToHashSet(); foreach (var item in items) if (itemIds.Add(item.Kind + ":" + item.Id)) Library.Add(item);
        HasMore = more; ApplyResultFilter(); Status = $"{Section} · {Results.Count} 首歌曲，{Library.Count} 个项目";
        if (!append && Application.Current.Windows.Cast<Window>().OfType<MainWindow>().Any(w => w.IsVisible)) Run(() => LoadVisibleCoversAsync(0, 20));
    }
    private static void Rows(System.Text.Json.JsonElement parent, string key, string kind, List<LibraryItem> items)
    {
        if (parent.TryGetProperty(key, out var rows)) foreach (var row in rows.EnumerateArray()) items.Add(new(NavidromeApiClient.Text(row, "id"), NavidromeApiClient.Text(row, "name"), NavidromeApiClient.Text(row, "artist", kind == "playlist" ? "歌单" : kind == "artist" ? "歌手" : "专辑"), kind, NavidromeApiClient.Text(row, "coverArt")));
    }
    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.CurrentCultureIgnoreCase);
    public void ApplyResultFilter()
    {
        CollectionViewSource.GetDefaultView(Results).Filter = row => row is Track t && ShowSongs && (FormatFilter == "全部格式" || t.Format.Equals(FormatFilter, StringComparison.OrdinalIgnoreCase)) && Contains(t.Artist, ArtistFilter) && Contains(t.Album, AlbumFilter);
        CollectionViewSource.GetDefaultView(Library).Filter = row => row is LibraryItem item && (SearchCategory == "全部" || Section != "搜索" || SearchCategory == "专辑" && item.Kind.EndsWith("album") || SearchCategory == "歌手" && item.Kind.EndsWith("artist")) && Contains(item.Subtitle, ArtistFilter) && Contains(item.Title, AlbumFilter);
        Changed(nameof(ShowSongs)); Changed(nameof(ShowAlbums));
    }
    public async Task LoadVisibleCoversAsync(int first, int count)
    {
        int generation = _browseGeneration, viewport = ++_coverViewportGeneration; var token = _browseCts.Token;
        var visible = CollectionViewSource.GetDefaultView(Library).Cast<LibraryItem>().Skip(Math.Max(0, first - 4)).Take(Math.Min(60, count + 8)).ToArray();
        var keep = visible.ToHashSet(); foreach (var item in Library.Where(i => i.Cover != null && !keep.Contains(i))) item.Cover = null;
        await Task.WhenAll(visible.Where(i => i.Cover == null && (!string.IsNullOrEmpty(i.CoverArt) || i.LocalCoverPath != null)).Select(async item =>
        {
            try { var image = item.LocalCoverPath != null ? await Task.Run(() => CoverCacheService.Load(item.LocalCoverPath, 160), token) : await Covers.GetThumbnailAsync(item.CoverArt, Settings.CoverCacheMb, token); if (generation == _browseGeneration && viewport == _coverViewportGeneration && !token.IsCancellationRequested) item.Cover = image; }
            catch (OperationCanceledException) { } catch (ApiException) { }
        }));
    }
    public void ReleaseBrowseImages() { _coverViewportGeneration++; foreach (var item in Library) item.Cover = null; Covers.ClearDecoded(); }
    public async Task ClearSearchAsync()
    {
        if (Section != "搜索") return; var previous = _beforeSearch; _beforeSearch = null;
        if (previous is { } old && _browseHistory.TryPeek(out var last) && last.Section == old.Section && last.Item?.Id == old.Item?.Id) { _browseHistory.Pop(); Changed(nameof(CanGoBack)); }
        if (previous is { } snapshot) { _formatFilter = snapshot.Format; _artistFilter = snapshot.Artist; _albumFilter = snapshot.Album; _searchCategory = snapshot.Category; Changed(nameof(FormatFilter)); Changed(nameof(ArtistFilter)); Changed(nameof(AlbumFilter)); Changed(nameof(SearchCategory)); }
        await BrowseAsync(previous?.Section ?? Navigation, item: previous?.Item, remember: false);
        if (previous is { } restored) RestoreFilters(restored.Format, restored.Artist, restored.Album, restored.Category);
    }
    private void RestoreFilters(string format, string artist, string album, string category)
    {
        _formatFilter = format; _artistFilter = artist; _albumFilter = album; _searchCategory = category;
        Changed(nameof(FormatFilter)); Changed(nameof(ArtistFilter)); Changed(nameof(AlbumFilter)); Changed(nameof(SearchCategory)); ApplyResultFilter();
    }
    public async Task GoBackAsync()
    {
        if (!_browseHistory.TryPop(out var previous)) return;
        Changed(nameof(CanGoBack)); await BrowseAsync(previous.Section, previous.Query, previous.Item, remember: false);
        RestoreFilters(previous.Format, previous.Artist, previous.Album, previous.Category);
    }
    public Task PlayBrowseTrackAsync(Track track) => PlayFromListAsync(track, CollectionViewSource.GetDefaultView(Results).Cast<Track>().ToArray());
    public async Task PlayLibraryAsync(LibraryItem item, bool enqueue = false)
    {
        List<Track> tracks;
        if (item.Kind is "local-album" or "local-artist") tracks = _localTracks.Where(t => item.Kind == "local-album" ? t.Album == item.Title : t.Artist == item.Title).OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList();
        else if (item.Kind == "local-playlist") tracks = Playlists.Local.FirstOrDefault(p => p.Id == item.Id)?.Tracks.ToList() ?? [];
        else if (item.Kind is "album" or "playlist") { var root = await Api.CallAsync(item.Kind == "album" ? "getAlbum" : "getPlaylist", default, ("id", item.Id)); tracks = NavidromeApiClient.Tracks(root, item.Kind == "album" ? "album" : "playlist", item.Kind == "album" ? "song" : "entry"); if (item.Kind == "album") tracks = tracks.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ToList(); }
        else { await BrowseAsync(item.Title, item: item); return; }
        if (tracks.Count == 0) { Status = "没有可播放的歌曲"; return; }
        if (enqueue) AddRange(tracks, false); else await PlayFromListAsync(tracks[0], tracks);
    }
    private void OnLibraryProgress(string message) => _dispatcher.BeginInvoke(() => Status = message);
    private void OnLocalLibraryUpdated(IReadOnlyList<Track> tracks)
    {
        void Apply()
        {
            if (_disposed) return;
            _localTracks.Clear(); _localTracks.AddRange(tracks);
            foreach (var old in Queue.Where(t => t.IsLocal).ToArray()) { var fresh = _localTracks.FirstOrDefault(t => t.Id == old.Id); if (fresh != null) { old.LocalPath = fresh.LocalPath; old.Missing = fresh.Missing; old.Title = fresh.Title; old.Artist = fresh.Artist; old.Album = fresh.Album; old.EmbeddedCoverPath = fresh.EmbeddedCoverPath; old.EmbeddedLyrics = fresh.EmbeddedLyrics; old.NotifyMetadata(); } }
            if (Navigation == "本地音乐" && Section != "搜索") Run(() => BrowseAsync("本地音乐"));
        }
        if (_dispatcher.CheckAccess()) Apply(); else _dispatcher.BeginInvoke(Apply);
    }
    public async Task AddFolderAsync(string path) { await LocalLibrary.AddFolderAsync(path); await BrowseAsync("本地音乐"); }
    public async Task DownloadSelectionAsync(IEnumerable<Track> tracks) => await Offline.DownloadAsync(tracks);
    public async Task DownloadAlbumAsync(LibraryItem album)
    {
        if (album.Kind != "album") throw new ApiException("请选择专辑。"); var root = await Api.CallAsync("getAlbum", default, ("id", album.Id)); await Offline.DownloadAsync(NavidromeApiClient.Tracks(root, "album"));
    }
    public async Task AddToPlaylistAsync(string id, bool server, IEnumerable<Track> tracks)
    {
        var selected = tracks.ToList();
        if (server) { if (selected.Any(t => t.IsLocal || t.IsNetease != id.StartsWith("ncm:", StringComparison.Ordinal))) throw new ApiException("歌单只能添加相同音乐来源的歌曲。"); await Api.UpdatePlaylistAsync(id, add: selected.Select(t => t.Id)); }
        else { var playlist = Playlists.Local.First(p => p.Id == id); playlist.Tracks.AddRange(selected); Playlists.Save(); }
        Status = "已添加到歌单";
    }
}
