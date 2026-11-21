using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WidgetNotes
{
    /// <summary>Ein gefundener Platz: Position und Bildschirm.</summary>
    public readonly record struct Slot(Point Position, Forms.Screen Screen);

    /// <summary>Raster eines Bildschirms (Arbeitsbereich ohne Taskleiste).</summary>
    public sealed class ScreenGrid
    {
        public Forms.Screen Screen { get; }
        public Rect WorkArea { get; }
        public double OriginX { get; }
        public double OriginY { get; }
        public int Cols { get; }
        public int Rows { get; }

        public ScreenGrid(Forms.Screen screen)
        {
            Screen = screen;
            double s = GridLayout.Scale;
            var wa = screen.WorkingArea;
            WorkArea = new Rect(wa.Left / s, wa.Top / s, wa.Width / s, wa.Height / s);

            Cols = Math.Max(1, (int)Math.Floor((WorkArea.Width + GridLayout.Gap) / GridLayout.Step));
            Rows = Math.Max(1, (int)Math.Floor((WorkArea.Height + GridLayout.Gap) / GridLayout.Step));

            // Raster beginnt direkt oben links am Bildschirmrand.
            OriginX = WorkArea.Left;
            OriginY = WorkArea.Top;
        }

        /// <summary>
        /// Position einer Zelle. <see cref="GridLayout.FlushEnd"/> bedeutet „bündig am rechten bzw. unteren Rand“.
        /// </summary>
        public Point PositionOf(int gx, int gy, Size tile) => new(
            gx == GridLayout.FlushEnd ? WorkArea.Right - tile.Width : OriginX + gx * GridLayout.Step,
            gy == GridLayout.FlushEnd ? WorkArea.Bottom - tile.Height : OriginY + gy * GridLayout.Step);
    }

    /// <summary>Raster, in das die Kacheln einrasten (ähnlich macOS-Widgets).</summary>
    public static class GridLayout
    {
        public const double Unit = 77;           // eine Rasterzelle
        public const double Gap = 16;            // Abstand zwischen Kacheln
        public const double Step = Unit + Gap;
        public const double ShadowMargin = 16;   // transparenter Rand des Fensters für den Schatten

        /// <summary>Spezielle Rasterzelle: bündig am rechten/unteren Rand.</summary>
        public const int FlushEnd = -1;

        public static (int W, int H) Units(string size) => size switch
        {
            "Small" => (2, 2),
            "Large" => (4, 4),
            "Tall" => (2, 4),
            _ => (4, 2),
        };

        public const int MinUnits = 2;

        public static Size TileSize(string size) => TileSize(Units(size));

        public static Size TileSize((int W, int H) u) =>
            new(u.W * Unit + (u.W - 1) * Gap, u.H * Unit + (u.H - 1) * Gap);

        /// <summary>Pixelmaß → nächstes Rastermaß (mindestens <see cref="MinUnits"/>).</summary>
        public static int ToUnits(double length) =>
            Math.Max(MinUnits, (int)Math.Round((length + Gap) / Step));

        internal static double Scale
        {
            get
            {
                using var g = Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
        }

        public static Forms.Screen ScreenAt(Point dip)
        {
            double s = Scale;
            return Forms.Screen.FromPoint(new Drawing.Point((int)(dip.X * s), (int)(dip.Y * s)));
        }

        /// <summary>Kennung eines Bildschirms: Position + Größe in Pixeln (stabiler als der Gerätename).</summary>
        public static string BoundsKey(Forms.Screen s) =>
            $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}";

        /// <summary>Den gespeicherten Bildschirm wiederfinden – oder null, wenn er gerade fehlt.</summary>
        public static Forms.Screen? FindScreen(string? boundsKey, string? deviceName)
        {
            var screens = Forms.Screen.AllScreens;
            return screens.FirstOrDefault(s => boundsKey != null && BoundsKey(s) == boundsKey)
                ?? screens.FirstOrDefault(s => deviceName != null && s.DeviceName == deviceName);
        }

        public static bool IsOnScreen(Rect tile)
        {
            var center = new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
            return Forms.Screen.AllScreens.Any(s => new ScreenGrid(s).WorkArea.Contains(center));
        }

        /// <summary>Standardplatz: oben rechts auf dem Hauptbildschirm.</summary>
        public static Point DefaultSpot((int W, int H) units)
        {
            var grid = new ScreenGrid(Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);
            return grid.PositionOf(FlushEnd, 0, TileSize(units));
        }

        /// <summary>
        /// Freien Platz suchen, der <paramref name="desired"/> am nächsten liegt. Kandidaten sind die Rasterzellen,
        /// die Bildschirmränder und – magnetisch – die Plätze direkt neben bzw. bündig mit anderen Kacheln.
        /// Ohne <paramref name="screen"/> wird der Bildschirm unter der Kachelmitte genommen.
        /// </summary>
        public static Slot FindSlot(Point desired, (int W, int H) units, IEnumerable<Rect> occupied, Forms.Screen? screen = null)
        {
            var tile = TileSize(units);
            screen ??= ScreenAt(new Point(desired.X + tile.Width / 2, desired.Y + tile.Height / 2));
            var grid = new ScreenGrid(screen);
            var wa = grid.WorkArea;
            var others = occupied.Where(o => o.IntersectsWith(wa)).ToList();

            var xs = Candidates(grid.OriginX, wa.Left, wa.Right, tile.Width, others.SelectMany(o => new[]
            {
                o.Right + Gap, o.Left - Gap - tile.Width, // daneben
                o.Left, o.Right - tile.Width,             // bündig
            }));
            var ys = Candidates(grid.OriginY, wa.Top, wa.Bottom, tile.Height, others.SelectMany(o => new[]
            {
                o.Bottom + Gap, o.Top - Gap - tile.Height,
                o.Top, o.Bottom - tile.Height,
            }));

            Point? best = null, nearest = null;
            double bestDist = double.MaxValue, nearestDist = double.MaxValue;
            foreach (double y in ys)
            {
                foreach (double x in xs)
                {
                    double d = (x - desired.X) * (x - desired.X) + (y - desired.Y) * (y - desired.Y);
                    if (d < nearestDist) { nearestDist = d; nearest = new Point(x, y); }
                    if (d >= bestDist) continue;

                    var inner = Rect.Inflate(new Rect(x, y, tile.Width, tile.Height), -2, -2);
                    if (others.Any(o => o.IntersectsWith(inner))) continue;
                    bestDist = d;
                    best = new Point(x, y);
                }
            }

            // Alles voll: einfach auf den nächsten Platz legen.
            return new Slot(best ?? nearest ?? new Point(wa.Left, wa.Top), screen);
        }

        /// <summary>Rasterpositionen, beide Ränder und Andock-Positionen – nur was auf den Bildschirm passt.</summary>
        private static List<double> Candidates(double origin, double min, double max, double length, IEnumerable<double> magnets)
        {
            double last = max - length;
            var list = new List<double> { min, last };
            for (double p = origin; p <= last + 0.5; p += Step) list.Add(p);
            list.AddRange(magnets.Where(p => p >= min - 0.5 && p <= last + 0.5));
            return list.Select(p => Math.Round(p, 1)).Distinct().ToList();
        }
    }
}
