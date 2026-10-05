using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace ChiliMusic;

public sealed class PlaybackQueue : ObservableCollection<Track>
{
    public void ReplaceWith(IEnumerable<Track> tracks)
    {
        var copy = tracks.ToList(); CheckReentrancy(); Items.Clear(); foreach (var track in copy) Items.Add(track);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count))); OnPropertyChanged(new PropertyChangedEventArgs("Item[]")); OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
