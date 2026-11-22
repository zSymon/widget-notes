using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WidgetNotes.Spotify
{
    /// <summary>Farben aus dem Cover: dunkler Grundton, Leuchten, kräftiger Akzent.</summary>
    public sealed record CoverPalette(Color Base, Color Glow, Color Accent)
    {
        public static readonly CoverPalette Default = new(Color.FromRgb(24, 32, 28), Color.FromRgb(58, 92, 70), Color.FromRgb(134, 214, 160));
        private static readonly CoverPalette Grey = new(Color.FromRgb(26, 27, 30), Color.FromRgb(70, 72, 80), Color.FromRgb(220, 222, 228));

        public static CoverPalette From(BitmapSource? cover)
        {
            if (cover == null) return Default;
            try
            {
                const int size = 32;
                var scaled = new TransformedBitmap(cover, new ScaleTransform((double)size / cover.PixelWidth, (double)size / cover.PixelHeight));
                var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);

                // Farbtöne in 24 Eimer, gewichtet nach Sättigung – so gewinnt die „Charakterfarbe“, nicht Grau.
                var bw = new double[24];
                var bs = new double[24];
                var bh = new double[24];
                double grey = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    var (hue, s, l) = ToHsl(px[i + 2], px[i + 1], px[i]);
                    if (l < 0.08 || l > 0.95) continue;
                    if (s < 0.15) { grey++; continue; }
                    double wgt = s * s * (1 - Math.Abs(l - 0.5));
                    int b = (int)(hue / 15) % 24;
                    bw[b] += wgt;
                    bs[b] += s * wgt;
                    bh[b] += hue * wgt;
                }
                int best = 0;
                for (int i = 1; i < 24; i++) if (bw[i] > bw[best]) best = i;
                if (bw[best] < 2 || bw[best] < grey * 0.02) return Grey;

                double hh = bh[best] / bw[best];
                double ss = Math.Min(0.75, bs[best] / bw[best]);
                return new CoverPalette(FromHsl(hh, ss * 0.55, 0.11), FromHsl(hh, ss * 0.8, 0.3), FromHsl(hh, Math.Max(0.55, ss), 0.68));
            }
            catch
            {
                return Default;
            }
        }

        private static (double H, double S, double L) ToHsl(byte rb, byte gb, byte bb)
        {
            double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2;
            if (max == min) return (0, 0, l);
            double d = max - min;
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double a = s * Math.Min(l, 1 - l);
            byte F(double n)
            {
                double k = (n + h / 30) % 12;
                return (byte)Math.Round((l - a * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1)))) * 255);
            }
            return Color.FromRgb(F(0), F(8), F(4));
        }
    }
}
