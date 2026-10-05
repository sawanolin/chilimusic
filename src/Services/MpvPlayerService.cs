using System.Runtime.InteropServices;
namespace ChiliMusic;

public interface IAudioPlayer : IDisposable
{
    void Load(string url, double start = 0);
    void Pause(); void Resume(); void Stop(); void Seek(double seconds);
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    bool IsPaused { get; }
    bool IsIdle { get; }
    event Action? Loaded; event Action<int>? Ended;
}
public sealed class MpvPlayerService : IAudioPlayer
{
    private IntPtr _handle;
    private readonly Thread _events;
    private volatile bool _disposed;
    private readonly System.Collections.Concurrent.ConcurrentQueue<int> _eventTrace = new();
    public int[] EventTrace => _eventTrace.ToArray();
    public event Action? Loaded;
    public event Action<int>? Ended;
    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent { public int Id; public int Error; public ulong UserData; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct EndFile { public int Reason; public int Error; public long PlaylistEntryId; public long PlaylistInsertId; public int PlaylistInsertNumEntries; }
    [StructLayout(LayoutKind.Sequential)] private struct LogMessage { public IntPtr Prefix; public IntPtr Level; public IntPtr Text; public int LogLevel; }
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_request_log_messages(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr h);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_wakeup(IntPtr h);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_command(IntPtr h, IntPtr args);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr p);
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Node { [FieldOffset(0)] public IntPtr Pointer; [FieldOffset(0)] public long Integer; [FieldOffset(0)] public double Number; [FieldOffset(8)] public int Format; }
    [StructLayout(LayoutKind.Sequential)] private struct NodeList { public int Count; public IntPtr Values, Keys; }
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_get_property(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out Node value);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_free_node_contents(ref Node node);
    [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern ulong mpv_client_api_version();
    public string Version => Get("mpv-version");
    public void EnableDiagnostics() => mpv_request_log_messages(_handle, "trace");
    public MpvPlayerService(AppSettings settings, bool nullAudio = false)
    {
        _handle = mpv_create(); if (_handle == IntPtr.Zero) throw new InvalidOperationException("无法创建 libmpv。");
        try
        {
            Option("config", "no"); Option("terminal", "no"); Option("msg-level", "all=no"); Option("vid", "no"); Option("idle", "yes"); Option("ytdl", "no"); Option("curl-enabled", "no");
            Option("ao", nullAudio ? "null" : "wasapi"); Option("audio-exclusive", settings.Exclusive ? "yes" : "no");
            Option("audio-device", settings.AudioDevice); Option("replaygain", settings.ReplayGain); Option("replaygain-preamp", settings.ReplayGainPreamp.ToString(System.Globalization.CultureInfo.InvariantCulture)); Option("replaygain-clip", "no"); Option("prefetch-playlist", settings.PrefetchNext ? "yes" : "no");
            Option("tls-verify", settings.AllowUntrustedCertificate ? "no" : "yes"); Option("cache", "yes"); Option("cache-secs", "8"); Option("demuxer-max-bytes", "16777216");
            Option("demuxer-max-back-bytes", "4194304"); Option("network-timeout", "15"); Option("gapless-audio", "yes"); Option("volume", settings.Volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Option("mute", settings.Mute ? "yes" : "no");
            if (mpv_initialize(_handle) < 0) throw new InvalidOperationException("libmpv 初始化失败。");
        }
        catch { mpv_terminate_destroy(_handle); _handle = IntPtr.Zero; throw; }
        _events = new Thread(EventLoop) { IsBackground = true, Name = "libmpv events" }; _events.Start();
    }
    private void Option(string name, string value) { if (mpv_set_option_string(_handle, name, value) < 0) throw new InvalidOperationException($"libmpv 不支持选项 {name}。"); }
    public void Set(string name, string value) { if (_disposed) return; if (mpv_set_property_string(_handle, name, value) < 0) throw new InvalidOperationException($"无法设置播放属性 {name}。"); }
    public string Get(string name)
    {
        if (_disposed) return ""; var ptr = mpv_get_property_string(_handle, name); if (ptr == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(ptr) ?? ""; } finally { mpv_free(ptr); }
    }
    private double Number(string name) => double.TryParse(Get(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    public double Position => Number("time-pos"); public double Duration => Number("duration");
    public double Volume { get => Number("volume"); set => Set("volume", Math.Clamp(value, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public bool IsPaused => Get("pause") == "yes"; public bool IsIdle => Get("idle-active") == "yes";
    public void Command(params string[] args)
    {
        if (_disposed) return; var strings = args.Select(Marshal.StringToCoTaskMemUTF8).ToArray(); var array = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try { for (int i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, strings[i]); Marshal.WriteIntPtr(array, strings.Length * IntPtr.Size, IntPtr.Zero); if (mpv_command(_handle, array) < 0) throw new InvalidOperationException("播放器命令执行失败。"); }
        finally { Marshal.FreeHGlobal(array); foreach (var p in strings) Marshal.FreeCoTaskMem(p); }
    }
    public void Load(string url, double start = 0) { Command("loadfile", url, "replace", "-1", "start=" + start.ToString(System.Globalization.CultureInfo.InvariantCulture)); Set("pause", "no"); }
    public void PrepareNext(string? url) { Command("playlist-clear"); if (url != null) Command("loadfile", url, "append", "-1", "start=0"); }
    public IReadOnlyList<AudioDevice> AudioDevices()
    {
        var devices = new List<AudioDevice> { new("auto", "系统默认输出") };
        if (mpv_get_property(_handle, "audio-device-list", 6, out var node) < 0) return devices;
        try { if (ReadNode(node) is List<object?> rows) foreach (var row in rows.OfType<Dictionary<string, object?>>()) if (row.TryGetValue("name", out var name) && name is string id && id.StartsWith("wasapi/", StringComparison.Ordinal)) devices.Add(new(id, row.GetValueOrDefault("description") as string ?? id)); }
        finally { mpv_free_node_contents(ref node); }
        return devices;
    }
    private static object? ReadNode(Node node)
    {
        if (node.Format == 1) return Marshal.PtrToStringUTF8(node.Pointer); if (node.Format == 3) return node.Integer != 0; if (node.Format == 4) return node.Integer; if (node.Format == 5) return node.Number;
        if (node.Format is not (7 or 8) || node.Pointer == IntPtr.Zero) return null;
        var list = Marshal.PtrToStructure<NodeList>(node.Pointer); if (list.Count is < 0 or > 10000) return null;
        if (node.Format == 7) { var array = new List<object?>(); for (int i = 0; i < list.Count; i++) array.Add(ReadNode(Marshal.PtrToStructure<Node>(IntPtr.Add(list.Values, i * 16)))); return array; }
        var map = new Dictionary<string, object?>(); for (int i = 0; i < list.Count; i++) map[Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size)) ?? ""] = ReadNode(Marshal.PtrToStructure<Node>(IntPtr.Add(list.Values, i * 16))); return map;
    }
    public void Pause() => Set("pause", "yes"); public void Resume() => Set("pause", "no"); public void Stop() => Command("stop");
    public void Seek(double seconds) => Command("seek", Math.Max(0, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute+exact");
    private void EventLoop()
    {
        while (!_disposed)
        {
            var e = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_handle, -1)); if (_disposed) break;
            _eventTrace.Enqueue(e.Id); while (_eventTrace.Count > 30) _eventTrace.TryDequeue(out _);
            if (e.Id == 2 && e.Data != IntPtr.Zero) { var log = Marshal.PtrToStructure<LogMessage>(e.Data); var message = Marshal.PtrToStringUTF8(log.Text) ?? ""; message = System.Text.RegularExpressions.Regex.Replace(message, @"https?://[^\s'""\]]+", "<url>"); message = System.Text.RegularExpressions.Regex.Replace(message, @"/rest/[^\s'""\]]+", "<api-path>"); if (!message.Contains("token", StringComparison.OrdinalIgnoreCase) && !message.Contains("password", StringComparison.OrdinalIgnoreCase) && !message.Contains("t=", StringComparison.OrdinalIgnoreCase) && !message.Contains("s=", StringComparison.OrdinalIgnoreCase)) Store.Log("DEBUG", message.Trim()); }
            if (e.Id == 8) Loaded?.Invoke();
            if (e.Id == 7 && e.Data != IntPtr.Zero) { var end = Marshal.PtrToStructure<EndFile>(e.Data); if (end.Reason == 0) Ended?.Invoke(0); else if (end.Reason == 4) Ended?.Invoke(end.Error == 0 ? -1 : end.Error); }
        }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; mpv_wakeup(_handle); _events.Join(); mpv_terminate_destroy(_handle); _handle = IntPtr.Zero; }
}
