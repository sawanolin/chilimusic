using System.Windows.Controls;
using System.Windows.Threading;

namespace ChiliMusic;

public partial class MainWindow
{
    private bool _restoringBrowse;
    private int _browseRestoreVersion;
    private BrowseViewport? _pendingBrowseViewport;
    private static ScrollViewer? BrowseScroll(ListBox list) => UiVerification.FindAll<ScrollViewer>(list).FirstOrDefault();
    private ListBox VisibleSongList => Vm.Settings.DetailedTracks && _details != null ? _details : TracksList;
    private void InitializeBrowsePosition()
    {
        Vm.BrowseLeaving += () =>
        {
            Vm.BrowseViewport = _restoringBrowse && _pendingBrowseViewport != null ? _pendingBrowseViewport : new(BrowseScroll(VisibleSongList)?.VerticalOffset ?? 0,
                BrowseScroll(LibraryList)?.VerticalOffset ?? 0,
                VisibleSongList.SelectedItems.Cast<Track>().ToArray(), LibraryList.SelectedItem as LibraryItem);
            _pendingBrowseViewport = null; ++_browseRestoreVersion; _restoringBrowse = true; _coverTimer.Stop();
        };
        Vm.BrowseArrived += state =>
        {
            _pendingBrowseViewport = state;
            int version = ++_browseRestoreVersion;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (version != _browseRestoreVersion) return;
                UpdateLayout(); var songs = VisibleSongList; songs.SelectedItems.Clear();
                foreach (var song in state.SelectedSongs ?? [])
                    if (songs.Items.Contains(song)) songs.SelectedItems.Add(song);
                LibraryList.SelectedItem = state.SelectedAlbum;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (version != _browseRestoreVersion) return;
                    UpdateLayout();
                    BrowseScroll(songs)?.ScrollToVerticalOffset(state.Songs);
                    BrowseScroll(LibraryList)?.ScrollToVerticalOffset(state.Albums);
                    UpdateLayout();
                    _pendingBrowseViewport = null; _restoringBrowse = false; _coverTimer.Start();
                });
            });
        };
    }
}
