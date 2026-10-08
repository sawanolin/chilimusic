using System.Text.Json;
using System.Text.Json.Serialization;
namespace ChiliMusic;

public sealed class Track : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void NotifyMetadata() => PropertyChanged?.Invoke(this, new(null));
    public string? LocalPath { get; set; }
    [JsonIgnore] public bool IsLocal => !string.IsNullOrEmpty(LocalPath);
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumId { get; set; } = "";
    public string? CoverArt { get; set; }
    public string? Suffix { get; set; }
    public string? ContentType { get; set; }
    public double Duration { get; set; }
    public int BitRate { get; set; }
    public int SamplingRate { get; set; }
    public int BitDepth { get; set; }
    public int ChannelCount { get; set; }
    public string? Starred { get; set; }
    [JsonPropertyName("track")] public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public string Genre { get; set; } = "";
    public uint Year { get; set; }
    public string? EmbeddedCoverPath { get; set; }
    public string? EmbeddedLyrics { get; set; }
    public long FileSize { get; set; }
    public DateTime FileModifiedUtc { get; set; }
    public bool Missing { get; set; }
    public bool Preview { get; set; }
    [JsonIgnore] public bool IsNetease => Id.StartsWith("ncm:", StringComparison.Ordinal);
    public string Fingerprint { get; set; } = "";
    [JsonIgnore] public string Subtitle => string.Join(" · ", new[] { Artist, Album }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
    [JsonIgnore] public string Format => string.IsNullOrEmpty(Suffix) ? "未知格式" : Suffix.ToUpperInvariant();
    public static Track Parse(JsonElement e) => JsonSerializer.Deserialize<Track>(e, Store.JsonOptions)!;
}
public sealed class LibraryItem(string id, string title, string subtitle, string kind, string? coverArt = null) : System.ComponentModel.INotifyPropertyChanged
{
    public string? LocalCoverPath { get; init; }
    public string Id { get; } = id; public string Title { get; } = title; public string Subtitle { get; } = subtitle; public string Kind { get; } = kind; public string? CoverArt { get; } = coverArt;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private System.Windows.Media.ImageSource? _cover;
    public System.Windows.Media.ImageSource? Cover { get => _cover; set { _cover = value; PropertyChanged?.Invoke(this, new(nameof(Cover))); } }
    public override string ToString() => $"{Title}    {Subtitle}";
}
public enum PlayMode { Sequential, RepeatAll, RepeatOne, Shuffle }
public sealed record AudioField(string Label, string Value);
public sealed class AppSettings
{
    public string CatalogSource { get; set; } = "navidrome";
    public string NeteaseQuality { get; set; } = "auto";
    public bool NeteaseLyrics { get; set; } = true;
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string? ProtectedPassword { get; set; }
    public bool Remember { get; set; } = true;
    public bool AllowUntrustedCertificate { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartInTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool NotifyTrack { get; set; }
    public string Theme { get; set; } = "System";
    public double Volume { get; set; } = 80;
    public bool Mute { get; set; }
    public PlayMode Mode { get; set; }
    public int CoverCacheMb { get; set; } = 200;
    public bool Exclusive { get; set; }
    public string AudioDevice { get; set; } = "auto";
    public string ReplayGain { get; set; } = "no";
    public double ReplayGainPreamp { get; set; }
    public bool PrefetchNext { get; set; } = true;
    public bool TaskbarEnabled { get; set; } = true;
    public int TaskbarWidth { get; set; } = 316;
    public bool TaskbarShowArtist { get; set; } = true;
    public bool TaskbarShowCover { get; set; } = true;
    public bool TaskbarAllMonitors { get; set; }
    public bool HotkeysEnabled { get; set; } = true;
    public int PlayKey { get; set; } = 0x76;
    public int PauseKey { get; set; } = 0x77;
    public bool RemapHardwareShortcuts { get; set; } = true;
    public bool CoverAccent { get; set; }
    public int OfflineCacheMb { get; set; } = 2048;
    public List<string> MusicFolders { get; set; } = [];
    public string AlbumSort { get; set; } = "newest";
}
public sealed class QueueState
{
    public List<Track> Tracks { get; set; } = [];
    public int Index { get; set; } = -1;
    public double Position { get; set; }
    public List<int> History { get; set; } = [];
    public int HistoryPosition { get; set; } = -1;
    public List<int> ShuffleRemaining { get; set; } = [];
    public List<int> PlayNext { get; set; } = [];
}

public sealed record AudioDevice(string Name, string Description) { public override string ToString() => Description; }
public sealed class SavedPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新歌单";
    public List<Track> Tracks { get; set; } = [];
}
public sealed class OfflineEntry
{
    public string Scope { get; set; } = "";
    public Track Track { get; set; } = new();
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
}
