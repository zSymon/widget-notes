using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WidgetNotes
{
    /// <summary>Raster, in das die Kacheln einrasten (ähnlich macOS-Widgets).</summary>
    public static class GridLayout
    {
        public const double Unit = 77;           // eine Rasterzelle
        public const double Gap = 16;            // Abstand zwischen Kacheln
        public const double Step = Unit + Gap;
        public const double EdgeMargin = 24;     // Mindestabstand zum Bildschirmrand
        public const double ShadowMargin = 16;   // transparenter Rand des Fensters für den Schatten

        public static readonly string[] Sizes = { "Small", "Medium", "Large", "Tall" };

        public static (int W, int H) Units(string size) => size switch
        {
            "Small" => (2, 2),
            "Large" => (4, 4),
            "Tall" => (2, 4),
            _ => (4, 2),
        };

        public static Size TileSize(string size)
        {
            var (w, h) = Units(size);
            return new Size(w * Unit + (w - 1) * Gap, h * Unit + (h - 1) * Gap);
        }

        private static double Scale
        {
            get
            {
                using var g = Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
        }

        public static Rect WorkAreaAt(Point dip)
        {
            double s = Scale;
            var screen = Forms.Screen.FromPoint(new Drawing.Point((int)(dip.X * s), (int)(dip.Y * s)));
            var wa = screen.WorkingArea;
            return new Rect(wa.Left / s, wa.Top / s, wa.Width / s, wa.Height / s);
        }

        public static bool IsOnScreen(Rect tile)
        {
            double s = Scale;
            return Forms.Screen.AllScreens.Any(scr =>
            {
                var wa = scr.WorkingArea;
                var r = new Rect(wa.Left / s, wa.Top / s, wa.Width / s, wa.Height / s);
                var center = new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
                return r.Contains(center);
            });
        }

        /// <summary>Standardplatz: oben rechts auf dem Hauptbildschirm.</summary>
        public static Point DefaultSpot(string size)
        {
            var wa = SystemParameters.WorkArea;
            var tile = TileSize(size);
            return new Point(wa.Right - tile.Width - EdgeMargin, wa.Top + EdgeMargin);
        }

        /// <summary>Freien Rasterplatz suchen, der <paramref name="desired"/> am nächsten liegt.</summary>
        public static Point FindSlot(Point desired, string size, IEnumerable<Rect> occupied)
        {
            var tile = TileSize(size);
            var others = occupied.ToList();
            var wa = WorkAreaAt(new Point(desired.X + tile.Width / 2, desired.Y + tile.Height / 2));

            int cols = Math.Max(1, (int)Math.Floor((wa.Width - 2 * EdgeMargin + Gap) / Step));
            int rows = Math.Max(1, (int)Math.Floor((wa.Height - 2 * EdgeMargin + Gap) / Step));

            // Raster mittig im Arbeitsbereich ausrichten.
            double ox = wa.Left + (wa.Width - (cols * Step - Gap)) / 2;
            double oy = wa.Top + (wa.Height - (rows * Step - Gap)) / 2;

            var (uw, uh) = Units(size);
            int maxX = Math.Max(0, cols - uw);
            int maxY = Math.Max(0, rows - uh);

            double fx = (desired.X - ox) / Step;
            double fy = (desired.Y - oy) / Step;

            Point? best = null;
            double bestDist = double.MaxValue;
            for (int gy = 0; gy <= maxY; gy++)
            {
                for (int gx = 0; gx <= maxX; gx++)
                {
                    var candidate = new Rect(ox + gx * Step, oy + gy * Step, tile.Width, tile.Height);
                    var inner = Rect.Inflate(candidate, -2, -2);
                    if (others.Any(o => o.IntersectsWith(inner))) continue;

                    double d = (gx - fx) * (gx - fx) + (gy - fy) * (gy - fy);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = candidate.TopLeft;
                    }
                }
            }

            if (best is Point p) return p;

            // Alles voll: einfach auf die nächste Zelle legen.
            int cx = (int)Math.Clamp(Math.Round(fx), 0, maxX);
            int cy = (int)Math.Clamp(Math.Round(fy), 0, maxY);
            return new Point(ox + cx * Step, oy + cy * Step);
        }
    }
}
