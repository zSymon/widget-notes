using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WidgetNotes
{
    /// <summary>Ein gefundener Rasterplatz: Position, Bildschirm und Zelle auf diesem Bildschirm.</summary>
    public readonly record struct Slot(Point Position, Forms.Screen Screen, int GridX, int GridY);

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

        public static Size TileSize(string size)
        {
            var (w, h) = Units(size);
            return new Size(w * Unit + (w - 1) * Gap, h * Unit + (h - 1) * Gap);
        }

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
        public static Point DefaultSpot(string size)
        {
            var grid = new ScreenGrid(Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);
            return grid.PositionOf(FlushEnd, 0, TileSize(size));
        }

        /// <summary>
        /// Freien Rasterplatz suchen, der <paramref name="desired"/> am nächsten liegt.
        /// Ohne <paramref name="screen"/> wird der Bildschirm unter der Kachelmitte genommen.
        /// </summary>
        public static Slot FindSlot(Point desired, string size, IEnumerable<Rect> occupied, Forms.Screen? screen = null)
        {
            var tile = TileSize(size);
            var others = occupied.ToList();
            screen ??= ScreenAt(new Point(desired.X + tile.Width / 2, desired.Y + tile.Height / 2));
            var grid = new ScreenGrid(screen);

            var (uw, uh) = Units(size);
            var xs = Candidates(grid.Cols - uw, grid.OriginX, grid.WorkArea.Right - tile.Width);
            var ys = Candidates(grid.Rows - uh, grid.OriginY, grid.WorkArea.Bottom - tile.Height);

            Slot? best = null, nearest = null;
            double bestDist = double.MaxValue, nearestDist = double.MaxValue;
            foreach (int gy in ys)
            {
                foreach (int gx in xs)
                {
                    var pos = grid.PositionOf(gx, gy, tile);
                    double d = (pos.X - desired.X) * (pos.X - desired.X) + (pos.Y - desired.Y) * (pos.Y - desired.Y);
                    if (d < nearestDist) { nearestDist = d; nearest = new Slot(pos, screen, gx, gy); }

                    var inner = Rect.Inflate(new Rect(pos, tile), -2, -2);
                    if (others.Any(o => o.IntersectsWith(inner))) continue;
                    if (d < bestDist) { bestDist = d; best = new Slot(pos, screen, gx, gy); }
                }
            }

            // Alles voll: einfach auf den nächsten Platz legen.
            return best ?? nearest!.Value;
        }

        /// <summary>Rasterzellen 0..max plus „bündig am Ende“, falls das nicht ohnehin auf eine Zelle fällt.</summary>
        private static List<int> Candidates(int max, double origin, double flushPos)
        {
            max = Math.Max(0, max);
            var list = Enumerable.Range(0, max + 1).ToList();
            if (flushPos - (origin + max * Step) > 1) list.Add(FlushEnd);
            return list;
        }
    }
}
