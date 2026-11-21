using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WidgetNotes
{
    /// <summary>Kern: Desktop-Integration, Raster, Ziehen, Text, Aussehen.</summary>
    public partial class NoteWindow : Window
    {
        private const double M = GridLayout.ShadowMargin;
        private static readonly CultureInfo German = new("de-DE");

        public NoteData Note { get; } = null!;

        private IntPtr _hwnd;
        private bool _loading = true;
        private bool _dragging;
        private bool _allowClose;
        private EventHandler? _moveAnimation;
        private readonly DropShadowEffect _shadow = null!;

        private App App => (App)Application.Current;

        public NoteWindow(NoteData note)
        {
            InitializeComponent();
            Note = note;
            if (Note.Modified == default) Note.Modified = DateTime.Now;
            _shadow = (DropShadowEffect)ShadowLayer.Effect;

            TitleBox.Text = note.Title;
            Editor.Text = note.Text;

            TodoList.ItemsSource = Note.Items;
            foreach (var item in Note.Items) item.PropertyChanged += Item_PropertyChanged;
            Note.Items.CollectionChanged += Items_CollectionChanged;

            ApplySizeDimensions();
            ApplyStyle();
            BuildSwatches();
            BuildFontOptions();
            Picker.ColorChanged += Picker_ColorChanged;
            ApplyKind();
            UpdatePlaceholders();

            LocationChanged += OnLocationChanged;
            _loading = false;
        }

        /// <summary>Logische Position/Größe der Kachel im Raster.</summary>
        public Rect TileRect
        {
            get
            {
                var s = GridLayout.TileSize(Note.Size);
                return new Rect(Note.X ?? Left + M, Note.Y ?? Top + M, s.Width, s.Height);
            }
        }

        // ---------- Desktop-Integration ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AttachToDesktop(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Kachel bleibt immer ganz unten (auf Desktop-Ebene), auch wenn man reinklickt.
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING && !_dragging)
            {
                var wp = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                if ((wp.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose && !App.IsExiting)
                e.Cancel = true;
            base.OnClosing(e);
        }

        // ---------- Raster & Position ----------

        public void PlaceInitial()
        {
            var size = GridLayout.TileSize(Note.Size);
            Point desired =
                Note.X is double x && Note.Y is double y && GridLayout.IsOnScreen(new Rect(x, y, size.Width, size.Height))
                    ? new Point(x, y)
                    : GridLayout.DefaultSpot(Note.Size);
            PlaceAt(desired, animate: false);
        }

        public void PlaceAt(Point desired, bool animate)
        {
            var slot = GridLayout.FindSlot(desired, Note.Size, App.OccupiedExcept(this));
            Note.X = slot.X;
            Note.Y = slot.Y;
            NoteStore.SaveSoon();

            double left = slot.X - M, top = slot.Y - M;
            if (animate && IsVisible) AnimateTo(left, top);
            else
            {
                StopMoveAnimation();
                Left = left;
                Top = top;
            }
        }

        private void ApplySizeDimensions()
        {
            var s = GridLayout.TileSize(Note.Size);
            Width = s.Width + 2 * M;
            Height = s.Height + 2 * M;
        }

        private void Body_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ConfirmOverlay.Visibility == Visibility.Visible) return;

            StopMoveAnimation();
            _dragging = true;
            SetLifted(true);
            try { DragMove(); } catch (InvalidOperationException) { }
            _dragging = false;
            SetLifted(false);
            App.Preview.Hide();

            PlaceAt(new Point(Left + M, Top + M), animate: true);
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            if (!_dragging) return;
            var slot = GridLayout.FindSlot(new Point(Left + M, Top + M), Note.Size, App.OccupiedExcept(this));
            App.Preview.ShowAt(new Rect(slot, GridLayout.TileSize(Note.Size)), _hwnd, Note.Radius);
        }

        private void AnimateTo(double left, double top)
        {
            StopMoveAnimation();
            double startLeft = Left, startTop = Top;
            if (Math.Abs(startLeft - left) < 0.5 && Math.Abs(startTop - top) < 0.5) return;

            var started = DateTime.Now;
            const double duration = 240;
            _moveAnimation = (_, _) =>
            {
                double t = Math.Min(1, (DateTime.Now - started).TotalMilliseconds / duration);
                double k = 1 - Math.Pow(1 - t, 3);
                Left = startLeft + (left - startLeft) * k;
                Top = startTop + (top - startTop) * k;
                if (t >= 1) StopMoveAnimation();
            };
            CompositionTarget.Rendering += _moveAnimation;
        }

        private void StopMoveAnimation()
        {
            if (_moveAnimation != null)
            {
                CompositionTarget.Rendering -= _moveAnimation;
                _moveAnimation = null;
            }
        }

        private void SetLifted(bool lifted)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(140);
            double scale = lifted ? 1.03 : 1.0;
            TileScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
            TileScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
            _shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(lifted ? 0.5 : 0.35, dur));
            _shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(lifted ? 6 : 3, dur));
        }

        // ---------- Text ----------

        private void Title_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholders();
            if (_loading) return;
            Note.Title = TitleBox.Text;
            Touch();
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholders();
            if (_loading) return;
            Note.Text = Editor.Text;
            Touch();
        }

        private void Touch()
        {
            Note.Modified = DateTime.Now;
            UpdateHeader();
            NoteStore.SaveSoon();
        }

        private void Title_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Down)
            {
                if (Note.Kind == "todo") FocusTodoStart();
                else
                {
                    Editor.Focus();
                    Editor.CaretIndex = 0;
                }
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                App.CreateNote(this);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ConfirmOverlay.Visibility == Visibility.Visible)
            {
                HideConfirm();
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Note.FontSize = Math.Clamp(Note.FontSize + (e.Delta > 0 ? 1 : -1), 10, 28);
            ApplyStyle();
            NoteStore.SaveSoon();
            e.Handled = true;
        }

        public void FocusEditor()
        {
            Activate();
            TitleBox.Focus();
        }

        private void UpdatePlaceholders()
        {
            TitlePlaceholder.Visibility = TitleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            EditorPlaceholder.Visibility = Editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateHeader()
        {
            if (Note.Kind == "todo")
            {
                int total = Note.Items.Count;
                int open = Note.Items.Count(i => !i.Done);
                int overdue = Note.Items.Count(i => i.IsOverdue);
                HeaderLabel.Text =
                    total == 0 ? "Aufgaben" :
                    open == 0 ? "Alles erledigt" :
                    overdue > 0 ? $"{open} offen · {overdue} überfällig" :
                    $"{open} offen";
                return;
            }

            var m = Note.Modified;
            HeaderLabel.Text =
                m.Date == DateTime.Today ? $"Heute, {m:HH:mm}" :
                m.Date == DateTime.Today.AddDays(-1) ? $"Gestern, {m:HH:mm}" :
                m.ToString("d. MMMM", German);
        }

        /// <summary>Wird vom App-Timer regelmäßig aufgerufen.</summary>
        public void RefreshTimeBased()
        {
            foreach (var item in Note.Items) item.Refresh();
            UpdateHeader();
        }

        // ---------- Aussehen ----------

        public void ApplyStyle()
        {
            var t = Themes.Resolve(Note);

            ShadowLayer.Background = t.CreateBackground(Note.Opacity);
            ShadowLayer.Effect = Note.Shadow ? _shadow : null;
            Gloss.Visibility = Note.Gloss ? Visibility.Visible : Visibility.Collapsed;

            Resources["Fg"] = Brush(t.Foreground);
            Resources["Secondary"] = Brush(t.Secondary);
            Resources["Accent"] = Brush(t.Accent);
            Resources["OnAccent"] = Brush(Themes.Luminance(t.Accent) > 0.55 ? Color.FromRgb(28, 28, 30) : Colors.White);

            // Im (immer dunklen) Menü braucht der Akzent genug Helligkeit.
            var menuAccent = Themes.Luminance(t.Accent) > 0.4 ? t.Accent : Color.FromRgb(0xFF, 0xD6, 0x0A);
            Resources["MenuAccent"] = Brush(menuAccent);
            Resources["OnMenuAccent"] = Brush(Themes.Luminance(menuAccent) > 0.55 ? Color.FromRgb(28, 28, 30) : Colors.White);

            Resources["ChipBg"] = Brush(t.IsDark ? Color.FromArgb(0x22, 255, 255, 255) : Color.FromArgb(0x16, 0, 0, 0));
            Resources["ChipHover"] = Brush(t.IsDark ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x2A, 0, 0, 0));

            Resources["Radius"] = new CornerRadius(Note.Radius);
            Resources["RadiusTop"] = new CornerRadius(Note.Radius, Note.Radius, 0, 0);

            var (display, text) = NoteFonts.Get(Note.Font);
            Resources["DisplayFont"] = display;
            Resources["UiFont"] = text;
            Resources["BodyFontSize"] = Note.FontSize;

            foreach (var box in new[] { TitleBox, Editor, AddBox })
            {
                box.CaretBrush = Brush(t.Foreground);
                box.SelectionBrush = Brush(t.Accent);
                box.SelectionOpacity = 0.35;
            }
        }

        private static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
