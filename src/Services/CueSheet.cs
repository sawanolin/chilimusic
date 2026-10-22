using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChiliMusic;

internal static class CueSheet
{
    internal static List<Track> Read(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new ApiException("CUE 文件过大。");
        var tracks = new List<Track>(); string album = "", albumArtist = "", audio = ""; uint year = 0; Track? current = null;
        string text = File.ReadAllText(path); if (text.Contains('\uFFFD')) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = File.ReadAllText(path, Encoding.GetEncoding(936)); }
        var originals = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n').Select(l => l.Trim()))
        {
            var m = Regex.Match(line, "^(FILE|TITLE|PERFORMER)\\s+(?:\"([^\"]*)\"|(.*))", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value.Trim();
                switch (m.Groups[1].Value.ToUpperInvariant())
                {
                    case "FILE": audio = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, value)); current = null; break;
                    case "TITLE": if (current == null) album = value; else current.Title = value; break;
                    case "PERFORMER": if (current == null) albumArtist = value; else current.Artist = value; break;
                }
                continue;
            }
            m = Regex.Match(line, @"^REM DATE\s+(\d{4})", RegexOptions.IgnoreCase); if (m.Success) uint.TryParse(m.Groups[1].Value, out year);
            m = Regex.Match(line, @"^TRACK\s+(\d+)\s+AUDIO", RegexOptions.IgnoreCase);
            if (m.Success && File.Exists(audio))
            {
                if (!originals.TryGetValue(audio, out var original)) originals[audio] = original = LocalLibraryService.ReadTrack(audio);
                current = JsonSerializer.Deserialize<Track>(JsonSerializer.Serialize(original))!;
                current.CuePath = Path.GetFullPath(path); current.Id = "cue:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(current.CuePath.ToUpperInvariant() + ":" + m.Groups[1].Value)));
                current.TrackNumber = uint.Parse(m.Groups[1].Value); current.Album = album.Length > 0 ? album : original.Album; current.Artist = albumArtist.Length > 0 ? albumArtist : original.Artist; current.AlbumArtist = albumArtist; current.Title = "曲目 " + current.TrackNumber; current.Year = year > 0 ? year : original.Year; current.Fingerprint = current.Id; tracks.Add(current);
            }
            m = Regex.Match(line, @"^INDEX\s+01\s+(\d+):(\d+):(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && current != null) current.StartOffset = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[3].Value) / 75d;
        }
        for (int i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i]; var next = tracks.Skip(i + 1).FirstOrDefault(n => n.LocalPath == t.LocalPath);
            t.EndOffset = next?.StartOffset ?? (t.Duration > 0 ? t.Duration : null); t.Duration = t.EndOffset.HasValue ? Math.Max(0, t.EndOffset.Value - t.StartOffset) : 0;
        }
        return tracks.Where(t => !t.EndOffset.HasValue || t.EndOffset > t.StartOffset).ToList();
    }
}
