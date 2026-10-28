using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChiliMusic;

// The atmosphere is painted once per cover/theme change, with no continuously running blur.
public sealed class CoverAtmosphere : FrameworkElement
{
    public static readonly DependencyProperty ArtworkProperty = DependencyProperty.Register(nameof(Artwork), typeof(ImageSource), typeof(CoverAtmosphere), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public ImageSource? Artwork { get => (ImageSource?)GetValue(ArtworkProperty); set => SetValue(ArtworkProperty, value); }
    private ArtworkPalette _palette;
    public CoverAtmosphere()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { Theme.Updated += Refresh; InvalidateVisual(); };
        Unloaded += (_, _) => Theme.Updated -= Refresh;
    }
    private void Refresh() => InvalidateVisual();
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    { var view = (CoverAtmosphere)owner; view._palette = e.NewValue is BitmapSource bitmap ? CoverPalette.Sample(bitmap) : default; }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing); if (_palette.Primary is not Color first || ActualWidth <= 0 || ActualHeight <= 0) return;
        void Glow(Color color, Point center, double radius)
        {
            var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = .95 };
            brush.GradientStops.Add(new(Color.FromArgb(Theme.IsDark ? (byte)49 : (byte)25, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new(Color.FromArgb(0, color.R, color.G, color.B), 1)); brush.Freeze();
            drawing.DrawRectangle(brush, null, new Rect(RenderSize));
        }
        Glow(first, new(.18, .10), .9);
        if (_palette.Secondary is Color second) Glow(second, new(.95, .25), .7);
    }
}
