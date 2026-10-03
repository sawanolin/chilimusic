using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
namespace ChiliMusic;
public sealed class CoverCacheService(NavidromeApiClient api)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(api.CacheScope + "\0" + id)));
    public async Task<string?> GetPathAsync(string? id, int limitMb, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id)) return null; await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(Store.Root, "cache", "covers"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Key(id) + ".img");
            if (File.Exists(path)) { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return path; }
            var bytes = await api.CoverAsync(id, ct); var image = Decode(bytes); if (image == null) return null;
            await File.WriteAllBytesAsync(path + ".tmp", bytes, ct); File.Move(path + ".tmp", path, true);
            var files = new DirectoryInfo(dir).EnumerateFiles("*.img").OrderBy(x => x.LastWriteTimeUtc).ToList(); long size = files.Sum(x => x.Length);
            foreach (var file in files) { if (size <= limitMb * 1024L * 1024L) break; size -= file.Length; file.Delete(); }
            return path;
        }
        finally { _gate.Release(); }
    }
    public static BitmapImage? Decode(byte[] bytes, int width = 480)
    {
        try { using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; } catch (Exception e) when (e is NotSupportedException or IOException or System.Runtime.InteropServices.COMException) { return null; }
    }
    public static BitmapImage? Load(string? path, int width = 480) => path != null && File.Exists(path) ? Decode(File.ReadAllBytes(path), width) : null;
}
