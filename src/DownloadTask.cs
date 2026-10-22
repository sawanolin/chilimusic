namespace ChiliMusic;

public sealed class DownloadTask : System.ComponentModel.INotifyPropertyChanged
{
    public Track Track { get; init; } = new();
    public string Scope { get; init; } = "";
    public string State { get; set; } = "等待";
    public long Bytes { get; set; }
    public long? Total { get; set; }
    public string Error { get; set; } = "";
    public bool Pinned { get; set; } = true;
    public double Percent => Total > 0 ? Math.Clamp(100d * Bytes / Total.Value, 0, 100) : 0;
    public string ActionGlyph => State is "下载中" or "等待" ? "Ⅱ" : State == "完成" ? "✓" : State == "失败" ? "↻" : "▶";
    public bool ActionEnabled => State != "完成";
    public string ActionName => State is "下载中" or "等待" ? "暂停下载" : "继续或重试下载";
    public string Detail => $"{State} · {(Total > 0 ? $"{Percent:0}%" : $"{Bytes / 1048576d:0.0} MB")}{(Error.Length == 0 ? "" : " · " + Error)}";
    public string Label => $"{Track.Title} · {State} · {(Total > 0 ? $"{100d * Bytes / Total:0}%" : $"{Bytes / 1048576d:0.0} MB")}{(Error.Length == 0 ? "" : " · " + Error)}";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(null));
}
