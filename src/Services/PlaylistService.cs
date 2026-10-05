using System.Text;
namespace ChiliMusic;

public sealed class PlaylistService
{
    public List<SavedPlaylist> Local { get; } = Store.Read("playlists.json", new List<SavedPlaylist>());
    public SavedPlaylist Create(string name, IEnumerable<Track> tracks)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ApiException("请输入歌单名称。");
        var playlist = new SavedPlaylist { Name = name.Trim(), Tracks = tracks.ToList() }; Local.Add(playlist); Save(); return playlist;
    }
    public void Save() => Store.Write("playlists.json", Local);
    public async Task ExportAsync(SavedPlaylist playlist, string destination, OfflineCacheService offline)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var track in playlist.Tracks)
        {
            string? path = track.LocalPath ?? offline.GetPath(track); if (path == null) continue;
            lines.Add($"#EXTINF:{(int)track.Duration},{track.Artist} - {track.Title}"); lines.Add(path);
        }
        if (lines.Count == 1) throw new ApiException("歌单里没有本地或已缓存的歌曲，请先下载后再导出。");
        await File.WriteAllLinesAsync(destination, lines, new UTF8Encoding(false));
    }
    public static async Task<List<string>> ImportPathsAsync(string file)
    {
        if (new FileInfo(file).Length > 10 * 1024 * 1024) throw new ApiException("播放列表超过 10 MB。");
        var lines = await File.ReadAllLinesAsync(file); var root = Path.GetDirectoryName(Path.GetFullPath(file))!; var paths = new List<string>();
        foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && !uri.IsFile) continue;
            try { string path = Path.GetFullPath(Uri.TryCreate(line, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : Path.Combine(root, line)); if (File.Exists(path) && LocalLibraryService.IsMusic(path)) paths.Add(path); } catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
