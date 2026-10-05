using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace ChiliMusic;

public static class Store
{
    public static string Root { get; } = ResolveRoot();
    private static string ResolveRoot() { var overridePath = Environment.GetEnvironmentVariable("CHILIMUSIC_DATA") ?? Environment.GetEnvironmentVariable("NAVIDROME_MINI_DATA"); if (!string.IsNullOrEmpty(overridePath)) return overridePath; var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); var root = Path.Combine(appData, "chilimusic"); if (!File.Exists(Path.Combine(root, "config.json"))) { var legacy = Path.Combine(appData, "NavidromeMiniPlayer"); if (Directory.Exists(legacy)) { Directory.CreateDirectory(root); foreach (var name in new[] { "config.json", "queue.json", "local-library.json" }) { var file = Path.Combine(legacy, name); if (File.Exists(file) && !File.Exists(Path.Combine(root, name))) File.Copy(file, Path.Combine(root, name)); } } } return root; }
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string name, T fallback)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Root, name)), JsonOptions) ?? fallback; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Log("WARNING", $"读取 {name} 失败，使用默认值"); return fallback; }
    }
    public static void Write<T>(string name, T value)
    {
        lock (WriteLock)
        {
        Directory.CreateDirectory(Root); var path = Path.Combine(Root, name); var tmp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOptions)); File.Move(tmp, path, true);
        }
    }
    private static readonly object WriteLock = new();
    private static readonly object LogLock = new();
    public static void Log(string level, string message)
    {
        lock (LogLock) try
            {
                var dir = Path.Combine(Root, "logs"); Directory.CreateDirectory(dir);
                foreach (var file in Directory.EnumerateFiles(dir, "*.log")) if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
                File.AppendAllText(Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), $"{DateTime.Now:HH:mm:ss} [{level}] {message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }
    public static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enable) key.SetValue("ChiliMusic", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("ChiliMusic", false);
    }
}

public static class CredentialService
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(bytes); }
    }
    public static string Protect(string password) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(password), true));
    public static string Unprotect(string? value) => string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
}
