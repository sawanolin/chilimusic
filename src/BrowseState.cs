namespace ChiliMusic;

// Session-only navigation state; it is never written to the player profile.
internal sealed record BrowseViewport(double Songs = 0, double Albums = 0,
    Track[]? SelectedSongs = null, LibraryItem? SelectedAlbum = null);

internal sealed record BrowseState(string Section, string Query, LibraryItem? Item,
    string Navigation, string Format, string Artist, string Album, string Category,
    string Sort, Track[] Songs, LibraryItem[] Albums,
    int SongOffset, int AlbumOffset, int ArtistOffset, bool HasMore, bool WasLoading,
    BrowseViewport Viewport);
