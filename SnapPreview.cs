using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WidgetNotes
{
    /// <summary>Halbtransparenter Platzhalter, der beim Ziehen den Zielplatz im Raster zeigt.</summary>
    public sealed class SnapPreview : Window
    {
        private IntPtr _hwnd;

        public SnapPreview()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Content = new Border
            {
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
            };
        }

        public bool IsClosed { get; private set; }

        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            base.OnClosed(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AttachToDesktop(_hwnd, clickThrough: true);
        }

        public void ShowAt(Rect r, IntPtr below, double radius)
        {
            ((Border)Content).CornerRadius = new CornerRadius(radius);
            Left = r.X;
            Top = r.Y;
            Width = r.Width;
            Height = r.Height;
            if (!IsVisible) Show();
            NativeMethods.SetWindowPos(_hwnd, below, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }
}
