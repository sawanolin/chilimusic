using System.Text.RegularExpressions;

namespace ChiliMusic;

public sealed partial class NeteaseClient
{
    public async Task<List<Track>> SearchLyricsAsync(string query, CancellationToken ct)
    {
        await EnsureAccountAsync(ct);
        if (string.IsNullOrWhiteSpace(query) || query.Length > 120) return [];
        var root = await RequestAsync("/api/cloudsearch/pc", new() { ["s"] = query, ["type"] = 1, ["limit"] = 30, ["offset"] = 0, ["total"] = true }, ct);
        return root.TryGetProperty("result", out var result) ? Rows(result, "songs").Select(Song).ToList() : [];
    }
    private static string Normal(string value) => Regex.Replace(value.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
    internal static Track? ChooseLyricsMatch(Track original, IEnumerable<Track> candidates)
    {
        // Keep version labels (live, remaster, instrumental) in the comparison.
        if (Normal(original.Title).Length == 0 || Normal(original.Artist).Length == 0) return null;
        var artists = Regex.Split(original.Artist, @"\s*[/、;&，]\s*").Select(Normal).Where(a => a.Length > 0).ToArray();
        var matches = candidates.Where(t => Normal(t.Title) == Normal(original.Title) && artists.All(a => Regex.Split(t.Artist, @"\s*[/、;&，]\s*").Select(Normal).Contains(a)) && (original.Duration <= 0 || t.Duration > 0 && Math.Abs(t.Duration - original.Duration) <= 5)).ToList();
        var sameAlbum = matches.Where(t => Normal(original.Album).Length > 0 && Normal(t.Album) == Normal(original.Album)).ToList();
        if (sameAlbum.Count == 1) return sameAlbum[0];
        return matches.Count == 1 ? matches[0] : null;
    }
    public async Task<Track?> MatchLyricsAsync(Track track, CancellationToken ct) => !LoggedIn ? null : ChooseLyricsMatch(track, await SearchLyricsAsync((track.Title + " " + track.Artist).Trim()[..Math.Min(120, (track.Title + " " + track.Artist).Trim().Length)], ct));
}
