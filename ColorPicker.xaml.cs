using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetNotes
{
    /// <summary>Kompakter HSV-Farbwähler mit Hex-Eingabe.</summary>
    public partial class ColorPicker : UserControl
    {
        private double _h, _s, _v = 1;

        public event EventHandler<Color>? ColorChanged;

        public Color SelectedColor => FromHsv(_h, _s, _v);

        public ColorPicker()
        {
            InitializeComponent();
            SvArea.SizeChanged += (_, _) => UpdateThumbs();
            HueArea.SizeChanged += (_, _) => UpdateThumbs();
            UpdateVisuals(raise: false);
        }

        public void SetColor(Color c)
        {
            ToHsv(c, out var h, out _s, out _v);
            if (_s > 0.001 && _v > 0.001) _h = h; // Farbton bei Grau/Schwarz beibehalten
            UpdateVisuals(raise: false);
        }

        private void UpdateVisuals(bool raise)
        {
            HueLayer.Background = new SolidColorBrush(FromHsv(_h, 1, 1));
            var c = SelectedColor;
            PreviewSwatch.Background = new SolidColorBrush(c);
            if (!HexBox.IsKeyboardFocused) HexBox.Text = Themes.ToHex(c);
            UpdateThumbs();
            if (raise) ColorChanged?.Invoke(this, c);
        }

        private void UpdateThumbs()
        {
            Canvas.SetLeft(SvThumb, _s * SvArea.ActualWidth - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _v) * SvArea.ActualHeight - SvThumb.Height / 2);
            Canvas.SetLeft(HueThumb, _h / 360 * HueArea.ActualWidth - HueThumb.Width / 2);
            Canvas.SetTop(HueThumb, (HueArea.ActualHeight - HueThumb.Height) / 2);
        }

        // ---------- Maus ----------

        private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SvArea.CaptureMouse();
            PickSv(e.GetPosition(SvArea));
            e.Handled = true;
        }

        private void Sv_MouseMove(object sender, MouseEventArgs e)
        {
            if (SvArea.IsMouseCaptured) PickSv(e.GetPosition(SvArea));
        }

        private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HueArea.CaptureMouse();
            PickHue(e.GetPosition(HueArea));
            e.Handled = true;
        }

        private void Hue_MouseMove(object sender, MouseEventArgs e)
        {
            if (HueArea.IsMouseCaptured) PickHue(e.GetPosition(HueArea));
        }

        private void Area_MouseUp(object sender, MouseButtonEventArgs e) =>
            ((UIElement)sender).ReleaseMouseCapture();

        private void PickSv(Point p)
        {
            _s = Math.Clamp(p.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
            _v = 1 - Math.Clamp(p.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
            UpdateVisuals(raise: true);
        }

        private void PickHue(Point p)
        {
            _h = Math.Clamp(p.X / Math.Max(1, HueArea.ActualWidth), 0, 0.9999) * 360;
            UpdateVisuals(raise: true);
        }

        // ---------- Hex ----------

        private void Hex_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHex();
        }

        private void Hex_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

        private void ApplyHex()
        {
            var text = HexBox.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(text);
                SetColor(Color.FromRgb(c.R, c.G, c.B));
                ColorChanged?.Invoke(this, SelectedColor);
            }
            catch (FormatException)
            {
                HexBox.Text = Themes.ToHex(SelectedColor);
            }
        }

        // ---------- HSV ----------

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = v - c;
            (double r, double g, double b) = (h / 60) switch
            {
                < 1 => (c, x, 0d),
                < 2 => (x, c, 0d),
                < 3 => (0d, c, x),
                < 4 => (0d, x, c),
                < 5 => (x, 0d, c),
                _ => (c, 0d, x),
            };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            h = d == 0 ? 0 :
                max == r ? 60 * (((g - b) / d) % 6) :
                max == g ? 60 * ((b - r) / d + 2) :
                60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            s = max == 0 ? 0 : d / max;
            v = max;
        }
    }
}
