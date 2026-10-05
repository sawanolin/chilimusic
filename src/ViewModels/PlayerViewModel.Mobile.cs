namespace ChiliMusic;

public sealed partial class PlayerViewModel
{
    public bool MobileOutput { get; private set; }
    public bool MobilePlaying { get; private set; }
    public int SeekRevision { get; private set; }
    public async Task SetMobileOutputAsync(bool phone)
    {
        if (MobileOutput == phone) return;
        bool playing = MobileOutput ? MobilePlaying : !Player.IsIdle && !Player.IsPaused;
        double position = Position; CancelPrepared();
        if (phone)
        {
            Player.Stop(); _timer.Stop(); _loaded = false; MobileOutput = true; MobilePlaying = playing;
            Quality = "手机播放"; Status = playing ? "正在手机播放" : "手机播放已暂停";
        }
        else
        {
            MobileOutput = false; MobilePlaying = false;
            if (Current != null) { _restorePosition = position; await PlayAsync(Index, true); if (!playing) Player.Pause(); }
        }
        Changed(nameof(MobileOutput)); Changed(nameof(PlayGlyph));
    }
    public void PausePlayback()
    {
        if (MobileOutput) MobilePlaying = false; else Player.Pause();
        if (MobileOutput) Status = "手机播放已暂停";
        Changed(nameof(PlayGlyph)); Media?.Status(MobileOutput ? Current == null : Player.IsIdle, true);
    }
    public async Task ResumePlaybackAsync()
    {
        if (MobileOutput) { if (Current == null) await ToggleAsync(); else { MobilePlaying = true; Status = "正在手机播放"; Changed(nameof(PlayGlyph)); Media?.Status(false, false); } }
        else if (Player.IsIdle) await ToggleAsync(); else { Player.Resume(); Changed(nameof(PlayGlyph)); Media?.Status(false, false); }
    }
    public void UpdateMobilePosition(double position)
    {
        if (!MobileOutput || !double.IsFinite(position)) return;
        if (!_mobileHistoryStarted && MobilePlaying && position > .1 && Current is { IsLocal: false } current) { _mobileHistoryStarted = true; if (current.IsNetease) Api.Netease.RecordRecent(current); Run(() => ScrobbleQuietlyAsync(current.Id, false)); }
        Position = Math.Clamp(position, 0, Duration); UpdateLyricPosition(); SaveQueue();
    }
    private bool _mobileHistoryStarted;
}
