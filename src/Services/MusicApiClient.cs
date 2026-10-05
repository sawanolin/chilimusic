using System.Text.Json;

namespace ChiliMusic;

public sealed class MusicApiClient : NavidromeApiClient
{
    private readonly AppSettings settings;
    public AppSettings Settings => settings;
    public MusicApiClient(AppSettings settings, string password) : base(settings, password) { this.settings = settings; }
    public NeteaseClient Netease { get; } = new();
    public bool UsingNetease => settings.CatalogSource == "netease";
    public bool NavidromeConfigured => base.Configured;
    public Task<JsonElement> CallSourceAsync(string source, string endpoint, CancellationToken ct = default, params (string Key, string Value)[] args) => source == "netease" ? Netease.SubsonicAsync(endpoint, args, ct) : base.CallAsync(endpoint, ct, args);
    public string SourceName => UsingNetease ? "网易云音乐" : "Navidrome";
    public override bool Configured => UsingNetease || base.Configured;
    public override string ScopeForId(string id) => id.StartsWith("ncm", StringComparison.Ordinal) ? "netease\0" + Netease.UserId : base.CacheScope;
    public override bool CacheScopeAllowed(string scope) => scope == base.CacheScope || scope == ScopeForId("ncm:");
    public override Task<string> StreamUrlAsync(Track track, CancellationToken ct = default) => track.IsNetease ? Netease.ResolveUrlAsync(track, settings.NeteaseQuality, false, ct) : base.StreamUrlAsync(track, ct);
    public override string Url(string endpoint, params (string Key, string Value)[] args)
    {
        if (args.Any(p => p.Value.StartsWith("ncm:", StringComparison.Ordinal))) throw new ApiException("网易云播放地址需要重新获取，请重试。");
        return base.Url(endpoint, args);
    }
    public override async Task<string> PingAsync(CancellationToken ct = default)
    {
        if (!UsingNetease) return await base.PingAsync(ct);
        if (Netease.LoggedIn) { await Netease.RefreshAccountAsync(ct); return "网易云音乐"; }
        await Netease.RequestAsync("/api/discovery/new/albums/area", new() { ["area"] = "ALL", ["limit"] = 1, ["offset"] = 0, ["type"] = "new", ["year"] = DateTime.Now.Year, ["month"] = DateTime.Now.Month }); return "网易云音乐 · 未登录";
    }
    public override Task<JsonElement> CallAsync(string endpoint, CancellationToken ct = default, params (string Key, string Value)[] args)
    {
        bool explicitNetease = args.Any(p => p.Key is "id" or "playlistId" && p.Value.StartsWith("ncm:", StringComparison.Ordinal));
        bool explicitNavidrome = args.Any(p => p.Key is "id" or "playlistId" && !p.Value.StartsWith("ncm:", StringComparison.Ordinal));
        if (explicitNetease || UsingNetease && !explicitNavidrome) return Netease.SubsonicAsync(endpoint, args, ct);
        return base.CallAsync(endpoint, ct, args);
    }
    public override Task<byte[]> CoverAsync(string id, CancellationToken ct) => id.StartsWith("ncm-cover:", StringComparison.Ordinal) ? Netease.CoverAsync(id[10..], ct) : base.CoverAsync(id, ct);
    public override Task<string?> ProbeTypeAsync(string id, CancellationToken ct) => id.StartsWith("ncm:", StringComparison.Ordinal) ? Task.FromResult<string?>(null) : base.ProbeTypeAsync(id, ct);
    internal override Task<HttpResponseMessage> OpenPhoneStreamAsync(Track track, bool mp3, string? range, CancellationToken ct) => track.IsNetease ? Netease.OpenAudioAsync(track, settings.NeteaseQuality, mp3, range, ct) : base.OpenPhoneStreamAsync(track, mp3, range, ct);
    public override Task DownloadAsync(string id, string destination, long maxBytes, IProgress<(long Bytes, long? Total)>? progress, CancellationToken ct)
    {
        if (id.StartsWith("ncm:", StringComparison.Ordinal)) throw new ApiException("网易云歌曲请使用官方客户端下载；可保存到本地歌单。");
        return base.DownloadAsync(id, destination, maxBytes, progress, ct);
    }
    public override void Dispose() { Netease.Dispose(); base.Dispose(); }
}
