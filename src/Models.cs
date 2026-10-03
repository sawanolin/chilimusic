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
    public string? CoverArt { get; set; }
    public string? Suffix { get; set; }
    public string? ContentType { get; set; }
    public double Duration { get; set; }
    public int BitRate { get; set; }
    public int SamplingRate { get; set; }
    public int BitDepth { get; set; }
    public int ChannelCount { get; set; }
    public string? Starred { get; set; }
    [JsonIgnore] public string Subtitle => string.Join(" · ", new[] { Artist, Album }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
    [JsonIgnore] public string Format => string.IsNullOrEmpty(Suffix) ? "未知格式" : Suffix.ToUpperInvariant();
    public static Track Parse(JsonElement e) => JsonSerializer.Deserialize<Track>(e, Store.JsonOptions)!;
}
public sealed class LibraryItem(string id, string title, string subtitle, string kind, string? coverArt = null) : System.ComponentModel.INotifyPropertyChanged
{
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
}
public sealed class QueueState
{
    public List<Track> Tracks { get; set; } = [];
    public int Index { get; set; } = -1;
    public double Position { get; set; }
}
