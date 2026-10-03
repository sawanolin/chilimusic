using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;
namespace ChiliMusic;
public sealed class MediaSessionService : IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    public event Action<SystemMediaTransportControlsButton>? ButtonPressed;
    public MediaSessionService(IntPtr hwnd)
    {
        _controls = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _controls.IsEnabled = true; _controls.IsPlayEnabled = true; _controls.IsPauseEnabled = true; _controls.IsNextEnabled = true; _controls.IsPreviousEnabled = true; _controls.IsStopEnabled = true;
        _controls.ButtonPressed += OnButton; _controls.PlaybackStatus = MediaPlaybackStatus.Stopped;
    }
    private void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs e) => ButtonPressed?.Invoke(e.Button);
    public async Task UpdateAsync(Track track, string? cover)
    {
        var updater = _controls.DisplayUpdater; updater.Type = MediaPlaybackType.Music; updater.MusicProperties.Title = track.Title; updater.MusicProperties.Artist = track.Artist; updater.MusicProperties.AlbumTitle = track.Album;
        updater.Thumbnail = null;
        if (cover != null) { var file = await StorageFile.GetFileFromPathAsync(cover); updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(file); }
        updater.Update();
    }
    public void Status(bool idle, bool paused) => _controls.PlaybackStatus = idle ? MediaPlaybackStatus.Stopped : paused ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing;
    public void Dispose() { _controls.ButtonPressed -= OnButton; _controls.IsEnabled = false; }
}
