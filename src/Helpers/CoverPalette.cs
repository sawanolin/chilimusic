// Adapted from Sonora crates/ui/src/palette.rs (GPL-3.0-or-later).
// Copyright (C) 2026 Sonora Contributors. See licenses/Sonora-NOTICE.md.
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChiliMusic;

internal readonly record struct ArtworkPalette(Color? Primary, Color? Secondary);

internal static class CoverPalette
{
    private const int Bins = 24;
    private sealed class Bin
    {
        public double Weight, X, Y, Saturation, Lightness;
        public void Add(double hue, double saturation, double lightness, double weight)
        { Weight += weight; X += Math.Cos(hue * Math.Tau) * weight; Y += Math.Sin(hue * Math.Tau) * weight; Saturation += saturation * weight; Lightness += lightness * weight; }
    }

    internal static ArtworkPalette Sample(BitmapSource image)
    {
        // Decode/sample a small frame; full-resolution cover art never allocates a second pixel buffer.
        double scale = Math.Min(1, 96d / Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var bitmap = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        var bins = Enumerable.Range(0, Bins).Select(_ => new Bin()).ToArray(); int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue;
            count++; ToHsl(Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]), out double h, out double s, out double l);
            if (s < .14 || l < .10 || l > .94) continue;
            bins[Math.Min(Bins - 1, (int)(h * Bins))].Add(h, s, l, s * (.3 + .7 * l));
        }
        double Score(int i) => bins[i].Weight + (bins[(i + Bins - 1) % Bins].Weight + bins[(i + 1) % Bins].Weight) * .5;
        int peak = Enumerable.Range(0, Bins).MaxBy(Score);
        if (Score(peak) < Math.Max(count * .01, 25)) return default;
        Color Cluster(int i)
        {
            var cluster = new[] { bins[(i + Bins - 1) % Bins], bins[i], bins[(i + 1) % Bins] }; double weight = cluster.Sum(b => b.Weight);
            double hue = Math.Atan2(cluster.Sum(b => b.Y), cluster.Sum(b => b.X)) / Math.Tau;
            return FromHsl((hue + 1) % 1, cluster.Sum(b => b.Saturation) / weight, cluster.Sum(b => b.Lightness) / weight);
        }
        Color primary = Cluster(peak);
        int runner = Enumerable.Range(0, Bins).Where(i => Math.Min(Math.Abs(i - peak), Bins - Math.Abs(i - peak)) >= 3 && Score(i) >= Math.Max(count * .02, 10)).OrderByDescending(Score).DefaultIfEmpty(-1).First();
        return new(primary, runner >= 0 ? Cluster(runner) : null);
    }
    internal static void ToHsl(Color color, out double hue, out double saturation, out double lightness)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d, high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b)), delta = high - low;
        lightness = (high + low) / 2; saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        hue = delta == 0 ? 0 : (high == r ? ((g - b) / delta + 6) % 6 : high == g ? (b - r) / delta + 2 : (r - g) / delta + 4) / 6;
    }
    internal static Color FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation, x = c * (1 - Math.Abs(hue * 6 % 2 - 1)), m = lightness - c / 2;
        var (r, g, b) = (int)(hue * 6) switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255), (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255), (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255));
    }
}
