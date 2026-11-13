using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ChiliMusic;

// Composite first, then dither: low-alpha gradients otherwise show opacity steps.
public sealed class CoverAtmosphere : FrameworkElement
{
    public static readonly DependencyProperty ArtworkProperty = DependencyProperty.Register(nameof(Artwork), typeof(ImageSource), typeof(CoverAtmosphere), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public ImageSource? Artwork { get => (ImageSource?)GetValue(ArtworkProperty); set => SetValue(ArtworkProperty, value); }
    private ArtworkPalette _palette;
    private BitmapSource? _image;
    public CoverAtmosphere()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { Theme.Updated += Refresh; Refresh(); };
        Unloaded += (_, _) => Theme.Updated -= Refresh;
    }
    private void Refresh() { _image = null; InvalidateVisual(); }
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var view = (CoverAtmosphere)owner;
        view._palette = e.NewValue is BitmapSource bitmap ? CoverPalette.Sample(bitmap) : default;
        view.Refresh();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_palette.Primary is not Color first || ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        if (_image == null || _image.PixelWidth != width || _image.PixelHeight != height || _image.DpiX != dpi.PixelsPerInchX || _image.DpiY != dpi.PixelsPerInchY)
        {
            var background = (TryFindResource("BackgroundBrush") as SolidColorBrush)?.Color ?? Colors.White;
            var pixels = new byte[width * height * 4];
            double opacity = Theme.IsDark ? 49d / 255 : 25d / 255;
            for (int y = 0; y < height; y++)
            {
                double py = (y + .5) / height;
                double fade = Math.Clamp((1 - py) / .35, 0, 1);
                fade = fade * fade * (3 - 2 * fade);
                for (int x = 0; x < width; x++)
                {
                    double px = (x + .5) / width;
                    double a = Glow(px, py, .18, .10, .9) * opacity * fade;
                    double r = background.R + (first.R - background.R) * a;
                    double g = background.G + (first.G - background.G) * a;
                    double b = background.B + (first.B - background.B) * a;
                    if (_palette.Secondary is Color second)
                    {
                        a = Glow(px, py, .95, .25, .7) * opacity * fade;
                        r += (second.R - r) * a; g += (second.G - g) * a; b += (second.B - b) * a;
                    }
                    // Stable sub-level noise avoids both bands and animated shimmer.
                    uint hash = unchecked((uint)(x + y * width) * 747796405u + 2891336453u);
                    hash = unchecked(((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u);
                    double noise = ((hash >> 22) & 1023) / 1024d - .5;
                    int index = (y * width + x) * 4;
                    pixels[index] = Quantize(b, noise); pixels[index + 1] = Quantize(g, noise); pixels[index + 2] = Quantize(r, noise); pixels[index + 3] = 255;
                }
            }
            _image = BitmapSource.Create(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Bgra32, null, pixels, width * 4);
            _image.Freeze();
        }
        drawing.DrawImage(_image, new Rect(RenderSize));
    }
    private static double Glow(double x, double y, double cx, double cy, double radius)
        => Math.Max(0, 1 - Math.Sqrt(Math.Pow((x - cx) / radius, 2) + Math.Pow((y - cy) / .95, 2)));
    private static byte Quantize(double value, double noise) => (byte)Math.Clamp(Math.Floor(value + noise + .5), 0, 255);
}
