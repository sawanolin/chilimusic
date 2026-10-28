using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChiliMusic;

// Recycled queue rows cancel work and clear images when they leave the visual tree.
public sealed class TrackArtwork : Border
{
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Track), typeof(TrackArtwork), new PropertyMetadata(null, (owner, _) => ((TrackArtwork)owner).Refresh()));
    public Track? Track { get => (Track?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    private CancellationTokenSource? _request;
    private readonly TextBlock _fallback = new() { Text = "♪", FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    public TrackArtwork()
    {
        CornerRadius = new CornerRadius(4); Child = _fallback;
        _fallback.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); SetResourceReference(BackgroundProperty, "SubtleBrush");
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { Cancel(); SetResourceReference(BackgroundProperty, "SubtleBrush"); _fallback.Visibility = Visibility.Visible; };
    }
    private void Cancel() { _request?.Cancel(); _request?.Dispose(); _request = null; }
    private async void Refresh()
    {
        Cancel(); SetResourceReference(BackgroundProperty, "SubtleBrush"); _fallback.Visibility = Visibility.Visible;
        if (!IsLoaded || Track is not Track track) return;
        var request = _request = new(); var token = request.Token;
        try
        {
            ImageSource? artwork;
            if (track.EmbeddedCoverPath is string path) artwork = await Task.Run(() => CoverCacheService.Load(path, 80), token);
            else if (!string.IsNullOrEmpty(track.CoverArt) && Application.Current is App { Vm: not null } app) artwork = await app.Vm.Covers.GetThumbnailAsync(track.CoverArt, app.Vm.Settings.CoverCacheMb, token);
            else return;
            if (token.IsCancellationRequested || !ReferenceEquals(_request, request) || artwork == null) return;
            var brush = new ImageBrush(artwork) { Stretch = Stretch.UniformToFill }; brush.Freeze(); Background = brush; _fallback.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ApiException or NotSupportedException) { }
    }
}
