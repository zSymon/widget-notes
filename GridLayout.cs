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

            Cols = Math.Max(1, (int)Math.Floor((WorkArea.Width - 2 * GridLayout.EdgeMargin + GridLayout.Gap) / GridLayout.Step));
            Rows = Math.Max(1, (int)Math.Floor((WorkArea.Height - 2 * GridLayout.EdgeMargin + GridLayout.Gap) / GridLayout.Step));

            // Raster mittig im Arbeitsbereich ausrichten.
            OriginX = WorkArea.Left + (WorkArea.Width - (Cols * GridLayout.Step - GridLayout.Gap)) / 2;
            OriginY = WorkArea.Top + (WorkArea.Height - (Rows * GridLayout.Step - GridLayout.Gap)) / 2;
        }

        public Point PositionOf(int gx, int gy) =>
            new(OriginX + gx * GridLayout.Step, OriginY + gy * GridLayout.Step);
    }

    /// <summary>Raster, in das die Kacheln einrasten (ähnlich macOS-Widgets).</summary>
    public static class GridLayout
    {
        public const double Unit = 77;           // eine Rasterzelle
        public const double Gap = 16;            // Abstand zwischen Kacheln
        public const double Step = Unit + Gap;
        public const double EdgeMargin = 24;     // Mindestabstand zum Bildschirmrand
        public const double ShadowMargin = 16;   // transparenter Rand des Fensters für den Schatten

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
            var tile = TileSize(size);
            return new Point(grid.WorkArea.Right - tile.Width - EdgeMargin, grid.WorkArea.Top + EdgeMargin);
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
            int maxX = Math.Max(0, grid.Cols - uw);
            int maxY = Math.Max(0, grid.Rows - uh);

            double fx = (desired.X - grid.OriginX) / Step;
            double fy = (desired.Y - grid.OriginY) / Step;

            Slot? best = null;
            double bestDist = double.MaxValue;
            for (int gy = 0; gy <= maxY; gy++)
            {
                for (int gx = 0; gx <= maxX; gx++)
                {
                    var pos = grid.PositionOf(gx, gy);
                    var inner = Rect.Inflate(new Rect(pos, tile), -2, -2);
                    if (others.Any(o => o.IntersectsWith(inner))) continue;

                    double d = (gx - fx) * (gx - fx) + (gy - fy) * (gy - fy);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = new Slot(pos, screen, gx, gy);
                    }
                }
            }

            if (best is Slot found) return found;

            // Alles voll: einfach auf die nächste Zelle legen.
            int cx = (int)Math.Clamp(Math.Round(fx), 0, maxX);
            int cy = (int)Math.Clamp(Math.Round(fy), 0, maxY);
            return new Slot(grid.PositionOf(cx, cy), screen, cx, cy);
        }
    }
}
