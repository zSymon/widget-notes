using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WidgetNotes
{
    /// <summary>Größe ziehen (Griff unten rechts) und automatisches Wachsen bei zu viel Inhalt.</summary>
    public partial class NoteWindow
    {
        private bool _resizing;
        private Point _resizeStartMouse;
        private Size _resizeStartSize;
        private bool _growCheckPending;

        private static readonly Size MinTile = GridLayout.TileSize((GridLayout.MinUnits, GridLayout.MinUnits));

        // ---------- Größe ziehen ----------

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ConfirmOverlay.Visibility == Visibility.Visible) return;
            StopMoveAnimation();
            _resizing = true;
            _dragging = true; // Vorschau darf unter der Kachel liegen
            _resizeStartMouse = e.GetPosition(this);
            _resizeStartSize = GridLayout.TileSize(Note.Units);
            ResizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void Grip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            var p = e.GetPosition(this);
            var origin = TileRect.TopLeft;
            var workArea = new ScreenGrid(GridLayout.ScreenAt(origin)).WorkArea;

            double w = Math.Clamp(_resizeStartSize.Width + p.X - _resizeStartMouse.X, MinTile.Width, Math.Max(MinTile.Width, workArea.Right - origin.X));
            double h = Math.Clamp(_resizeStartSize.Height + p.Y - _resizeStartMouse.Y, MinTile.Height, Math.Max(MinTile.Height, workArea.Bottom - origin.Y));
            Width = w + 2 * M;
            Height = h + 2 * M;

            var units = (GridLayout.ToUnits(w), GridLayout.ToUnits(h));
            App.Preview.ShowAt(new Rect(origin, GridLayout.TileSize(units)), _hwnd, Note.Radius);
        }

        private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_resizing) ResizeGrip.ReleaseMouseCapture(); // löst FinishResize über LostMouseCapture aus
        }

        private void Grip_LostMouseCapture(object sender, MouseEventArgs e) => FinishResize();

        private void FinishResize()
        {
            if (!_resizing) return;
            _resizing = false;
            _dragging = false;
            App.Preview.Hide();

            Note.W = GridLayout.ToUnits(ActualWidth - 2 * M);
            Note.H = GridLayout.ToUnits(ActualHeight - 2 * M);
            var origin = TileRect.TopLeft;
            ApplySizeDimensions();
            PlaceAt(origin, animate: true);
        }

        // ---------- Automatisch wachsen ----------

        /// <summary>Nach Textänderungen prüfen, ob der Inhalt unten herausläuft.</summary>
        private void ScheduleGrowCheck()
        {
            if (_loading || _growCheckPending) return;
            _growCheckPending = true;
            // Background: läuft erst, nachdem das Layout den neuen Text vermessen hat.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _growCheckPending = false;
                GrowIfOverflowing();
            });
        }

        private void GrowIfOverflowing()
        {
            if (_resizing || _dragging || !IsVisible) return;

            // Unten wird der Inhalt ausgeblendet – schon wachsen, bevor Text in diesen Bereich rutscht.
            bool overflowing = Note.Kind == "todo"
                ? ListContent.ExtentHeight > ListContent.ViewportHeight * 0.9
                : Editor.ExtentHeight > Editor.ViewportHeight * 0.86;
            if (!overflowing) return;

            var units = (Note.Units.W, Note.Units.H + 1);
            var origin = TileRect.TopLeft;
            var grown = new Rect(origin, GridLayout.TileSize(units));

            // Nur wachsen, wenn darunter Platz ist (Bildschirmrand, andere Kacheln).
            var workArea = new ScreenGrid(GridLayout.ScreenAt(origin)).WorkArea;
            if (grown.Bottom > workArea.Bottom + 1) return;
            var inner = Rect.Inflate(grown, -2, -2);
            if (App.OccupiedExcept(this).Any(o => o.IntersectsWith(inner))) return;

            Note.W = units.Item1;
            Note.H = units.Item2;
            ApplySizeDimensions();
            PlaceAt(origin, animate: false);
            ScheduleGrowCheck(); // falls es immer noch nicht reicht (z. B. viel Text eingefügt)
        }
    }
}
