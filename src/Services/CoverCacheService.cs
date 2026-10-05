using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
namespace ChiliMusic;
public sealed class CoverCacheService(NavidromeApiClient api)
{
    private readonly SemaphoreSlim _gate = new(4, 4);
    private readonly Dictionary<string, BitmapImage> _decoded = [];
    private DateTime _lastTrim = DateTime.MinValue;
    private readonly object _trimLock = new();
    private int _decodeGeneration;
    private string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(api.ScopeForId(id) + "\0" + id)));
    public async Task<string?> GetPathAsync(string? id, int limitMb, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return null; await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(Store.Root, "cache", "covers"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Key(id) + ".img");
            if (File.Exists(path)) { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(15)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return path; }
            var bytes = await api.CoverAsync(id, ct); if (Decode(bytes, 32) == null) return null;
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, bytes, ct); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
            lock (_trimLock)
            {
            if (DateTime.UtcNow - _lastTrim < TimeSpan.FromSeconds(15)) return path;
            _lastTrim = DateTime.UtcNow;
            var files = new DirectoryInfo(dir).EnumerateFiles("*.img").OrderBy(x => x.LastWriteTimeUtc).ToList(); long size = files.Sum(x => x.Length);
            foreach (var file in files) { if (size <= limitMb * 1024L * 1024L) break; if (file.FullName == path) continue; try { long length = file.Length; file.Delete(); size -= length; } catch (IOException) { } }
            }
            return path;
        }
        finally { _gate.Release(); }
    }
    public async Task<BitmapImage?> GetThumbnailAsync(string? id, int limitMb, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return null; string key = Key(id); int generation = _decodeGeneration;
        lock (_decoded) if (_decoded.TryGetValue(key, out var cached)) return cached;
        string? path = await GetPathAsync(id, limitMb, ct); var image = await Task.Run(() => Load(path, 160), ct);
        if (image != null) lock (_decoded) { if (generation == _decodeGeneration) { if (_decoded.Count >= 64) _decoded.Remove(_decoded.Keys.First()); _decoded[key] = image; } }
        return image;
    }
    public void ClearDecoded() { lock (_decoded) { _decodeGeneration++; _decoded.Clear(); } }
    public static BitmapImage? Decode(byte[] bytes, int width = 480)
    {
        try { using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; } catch (Exception e) when (e is NotSupportedException or IOException or System.Runtime.InteropServices.COMException) { return null; }
    }
    public static BitmapImage? Load(string? path, int width = 480) => path != null && File.Exists(path) ? Decode(File.ReadAllBytes(path), width) : null;
}
