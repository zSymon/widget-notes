using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WidgetNotes.Spotify;

namespace WidgetNotes
{
    /// <summary>Spotify-Player als Kachel: Cover, Titel, Fortschritt, Steuerung, Lautstärke.</summary>
    public partial class NoteWindow
    {
        public const string SpotifyKind = "spotify";
        private const string ClientIdHelpUrl = "https://github.com/zSymon/widget-notes#spotify-player";

        private bool IsPlayer => Note.Kind == SpotifyKind;
        private bool _playerAttached;
        private bool _syncingVolume;
        private int _volumeBeforeMute = 50;
        private string _playerMode = "";
        private BitmapSource? _shownCover;
        private CoverPalette? _shownPalette;
        private DispatcherTimer? _progressTimer;

        private static SpotifyPlayer Player => SpotifyPlayer.Instance;

        // ---------- Modus ----------

        private void ApplyPlayerKind()
        {
            bool player = IsPlayer;
            PlayerContent.Visibility = player ? Visibility.Visible : Visibility.Collapsed;
            HeaderPanel.Visibility = player ? Visibility.Collapsed : Visibility.Visible;
            TitleRow.Visibility = player ? Visibility.Collapsed : Visibility.Visible;
            Body.Cursor = Cursors.SizeAll;

            if (player && !_playerAttached)
            {
                _playerAttached = true;
                Player.Changed += OnPlayerChanged;
                SpotifyAuth.Changed += OnAuthChanged;
                PlayerContent.SizeChanged += PlayerContent_SizeChanged;
                _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _progressTimer.Tick += (_, _) => UpdateProgress();
                Player.Attach();
                OnPlayerChanged();
            }
            else if (!player && _playerAttached) DetachPlayer();

            ApplyStyle();
        }

        private void DetachPlayer()
        {
            if (!_playerAttached) return;
            _playerAttached = false;
            Player.Changed -= OnPlayerChanged;
            SpotifyAuth.Changed -= OnAuthChanged;
            PlayerContent.SizeChanged -= PlayerContent_SizeChanged;
            _progressTimer?.Stop();
            _progressTimer = null;
            _shownCover = null;
            _shownPalette = null;
            Player.Detach();
        }

        private void OnAuthChanged() => Dispatcher.BeginInvoke(SyncSpotifyPage);

        // ---------- Aussehen ----------

        /// <summary>Cover-Stil: Farben aus dem Cover statt aus dem Notiz-Design.</summary>
        private Theme? PlayerTheme()
        {
            if (!IsPlayer || !Note.CoverBackdrop) return null;
            var p = Player.Palette;
            var deep = Color.FromRgb((byte)(p.Base.R * 0.55), (byte)(p.Base.G * 0.55), (byte)(p.Base.B * 0.55));
            return new Theme("cover", "Cover", new[] { p.Base, deep },
                Themes.Parse("#F5F5F7"), Themes.Parse("#A6FFFFFF"), p.Accent, true);
        }

        private void ApplyBackdrop()
        {
            bool cover = IsPlayer && Note.CoverBackdrop;
            CoverBackdrop.Visibility = cover ? Visibility.Visible : Visibility.Collapsed;
            if (!cover) return;

            var glow = Player.Palette.Glow;
            var brush = new RadialGradientBrush
            {
                Center = new Point(0, 0),
                GradientOrigin = new Point(0, 0),
                RadiusX = 1.25,
                RadiusY = 1.1,
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, glow.R, glow.G, glow.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.75));
            brush.Freeze();
            BackdropGlow.Fill = brush;
            UpdateBackdropClip();
        }

        private void UpdateBackdropClip()
        {
            if (CoverBackdrop.Visibility != Visibility.Visible) return;
            double w = Body.ActualWidth, h = Body.ActualHeight;
            if (w <= 0 || h <= 0) return;
            CoverBackdrop.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Note.Radius, Note.Radius);
        }

        /// <summary>Wer ein Notiz-Design wählt, will es auch sehen – Cover-Hintergrund dann aus.</summary>
        private void LeaveCoverMode()
        {
            if (IsPlayer) Note.CoverBackdrop = false;
        }

        // ---------- Anzeige ----------

        private void OnPlayerChanged()
        {
            if (!_playerAttached) return;
            var p = Player;
            bool has = p.HasTrack;

            TrackTitle.Text = has ? p.Title : "Nichts läuft gerade";
            TrackArtist.Text = has
                ? (p.RemoteDevice != null ? $"{p.Artist}  ·  {p.RemoteDevice}" : p.Artist)
                : "Starte Musik in Spotify";
            OpenSpotifyButton.Visibility = !has && _playerMode != "compact" ? Visibility.Visible : Visibility.Collapsed;

            PlayIcon.Visibility = p.Playing ? Visibility.Collapsed : Visibility.Visible;
            PauseIcon.Visibility = p.Playing ? Visibility.Visible : Visibility.Collapsed;

            bool roomy = _playerMode is "wide" or "large" || (_playerMode == "tall" && PlayerContent.ActualWidth >= 200);
            ShuffleButton.Visibility = roomy && p.Shuffle != null ? Visibility.Visible : Visibility.Hidden;
            SetActive(ShuffleButton, p.Shuffle == true);
            LikeButton.Visibility = roomy && p.Liked != null ? Visibility.Visible : Visibility.Hidden;
            LikeButton.Content = p.Liked == true ? "" : "";
            SetActive(LikeButton, p.Liked == true);

            bool volumeRoom = _playerMode is not "compact";
            VolumeRow.Visibility = volumeRoom && p.Volume != null ? Visibility.Visible : Visibility.Collapsed;
            if (p.Volume is int vol)
            {
                _syncingVolume = true;
                VolumeSlider.Value = vol;
                _syncingVolume = false;
                VolumeText.Text = vol.ToString();
                MuteButton.Content = vol == 0 ? "" : vol < 34 ? "" : vol < 67 ? "" : "";
            }

            if (!ReferenceEquals(p.Cover, _shownCover)) ShowCover(p.Cover);
            if (!ReferenceEquals(p.Palette, _shownPalette))
            {
                _shownPalette = p.Palette;
                if (Note.CoverBackdrop) ApplyStyle();
            }

            if (p.Playing) _progressTimer?.Start();
            else _progressTimer?.Stop();
            UpdateProgress();
        }

        private void SetActive(Button b, bool active)
        {
            if (active) b.SetResourceReference(ForegroundProperty, "Accent");
            else b.ClearValue(ForegroundProperty);
        }

        private void ShowCover(BitmapSource? cover)
        {
            _shownCover = cover;
            if (cover == null)
            {
                CoverBox.SetResourceReference(Border.BackgroundProperty, "ChipBg");
                CoverPlaceholder.Visibility = Visibility.Visible;
                BackdropImage.Source = null;
                return;
            }

            var brush = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            CoverBox.Background = brush;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
            BackdropImage.Source = cover;

            // Sanft einblenden
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            CoverBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            BackdropImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.42, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease });
        }

        private void UpdateProgress()
        {
            if (!_playerAttached) return;
            var p = Player;
            var dur = p.Duration;
            var pos = p.Position;
            double pct = dur > TimeSpan.Zero ? Math.Clamp(pos / dur, 0, 1) : 0;
            ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * pct);
            PosText.Text = Fmt(pos);
            DurText.Text = dur > TimeSpan.Zero ? Fmt(dur) : "0:00";
        }

        private static string Fmt(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        // ---------- Layout je nach Kachelgröße ----------

        private void PlayerContent_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutPlayer();

        private void LayoutPlayer()
        {
            double w = PlayerContent.ActualWidth, h = PlayerContent.ActualHeight;
            if (w <= 0 || h <= 0) return;
            bool wide = w >= 250, tall = h >= 250;
            _playerMode = wide ? (tall ? "large" : "wide") : (tall ? "tall" : "compact");

            var g = PlayerGrid;
            g.RowDefinitions.Clear();
            g.ColumnDefinitions.Clear();
            foreach (UIElement child in g.Children)
            {
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, 0);
                Grid.SetRowSpan(child, 1);
                Grid.SetColumnSpan(child, 1);
                if (child is FrameworkElement fe) fe.Margin = new Thickness(0);
            }
            void Rows(params GridLength[] heights) { foreach (var x in heights) g.RowDefinitions.Add(new RowDefinition { Height = x }); }
            void Cols(params GridLength[] widths) { foreach (var x in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = x }); }
            void Put(FrameworkElement e, int row, int col = 0, int colSpan = 1, int rowSpan = 1, Thickness? margin = null)
            {
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                Grid.SetColumnSpan(e, colSpan);
                Grid.SetRowSpan(e, rowSpan);
                e.Margin = margin ?? new Thickness(0);
            }
            var auto = GridLength.Auto;
            var star = new GridLength(1, GridUnitType.Star);

            bool times = wide;
            PosText.Visibility = DurText.Visibility = times ? Visibility.Visible : Visibility.Collapsed;
            ProgressRow.Visibility = _playerMode == "compact" ? Visibility.Collapsed : Visibility.Visible;
            InfoPanel.VerticalAlignment = VerticalAlignment.Center;
            CoverBox.HorizontalAlignment = HorizontalAlignment.Left;
            CoverBox.VerticalAlignment = VerticalAlignment.Top;
            TrackTitle.TextWrapping = TextWrapping.NoWrap;
            TrackTitle.MaxHeight = double.PositiveInfinity;
            PlayButton.Width = PlayButton.Height = 40;
            double radius = Math.Max(6, Math.Min(14, Note.Radius * 0.55));
            CoverBox.CornerRadius = new CornerRadius(radius);

            switch (_playerMode)
            {
                case "compact": // 2×2
                {
                    Rows(auto, star, auto);
                    double c = Math.Min(56, h * 0.38);
                    CoverBox.Width = CoverBox.Height = c;
                    Put(CoverBox, 0);
                    Put(InfoPanel, 1, margin: new Thickness(0, 6, 0, 4));
                    InfoPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    TrackTitle.FontSize = 14;
                    Put(ControlsRow, 2, margin: new Thickness(-4, 0, -4, 0));
                    PlayButton.Width = PlayButton.Height = 34;
                    break;
                }
                case "wide": // 4×2
                {
                    Cols(auto, star);
                    Rows(star, auto, auto, auto);
                    CoverBox.Width = CoverBox.Height = h;
                    Put(CoverBox, 0, rowSpan: 4, margin: new Thickness(0, 0, 14, 0));
                    Put(InfoPanel, 0, 1);
                    TrackTitle.FontSize = 15;
                    Put(ProgressRow, 1, 1, margin: new Thickness(0, 0, 0, 2));
                    Put(ControlsRow, 2, 1, margin: new Thickness(-4, 0, -4, 2));
                    PlayButton.Width = PlayButton.Height = 36;
                    Put(VolumeRow, 3, 1, margin: new Thickness(-3, 0, 0, 0));
                    PosText.Visibility = DurText.Visibility = Visibility.Collapsed;
                    break;
                }
                case "tall": // 2×4
                {
                    Rows(auto, star, auto, auto, auto);
                    double c = Math.Min(w, h * 0.5);
                    CoverBox.Width = CoverBox.Height = c;
                    CoverBox.HorizontalAlignment = HorizontalAlignment.Center;
                    Put(CoverBox, 0);
                    Put(InfoPanel, 1, margin: new Thickness(0, 10, 0, 6));
                    TrackTitle.FontSize = 15;
                    TrackTitle.TextWrapping = TextWrapping.Wrap;
                    TrackTitle.MaxHeight = 42;
                    Put(ProgressRow, 2, margin: new Thickness(0, 0, 0, 4));
                    Put(ControlsRow, 3, margin: new Thickness(-4, 0, -4, 6));
                    PlayButton.Width = PlayButton.Height = 38;
                    Put(VolumeRow, 4, margin: new Thickness(-3, 0, 0, 0));
                    break;
                }
                default: // groß
                {
                    Cols(auto, star);
                    Rows(auto, star, auto, auto, auto);
                    double c = Math.Min(Math.Min(w * 0.46, h * 0.46), 220);
                    CoverBox.Width = CoverBox.Height = c;
                    Put(CoverBox, 0, margin: new Thickness(0, 0, 16, 0));
                    Put(InfoPanel, 0, 1);
                    InfoPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    TrackTitle.FontSize = Math.Min(22, 15 + w / 100);
                    TrackTitle.TextWrapping = TextWrapping.Wrap;
                    TrackTitle.MaxHeight = TrackTitle.FontSize * 2.7;
                    Put(ProgressRow, 2, colSpan: 2, margin: new Thickness(0, 0, 0, 6));
                    Put(ControlsRow, 3, colSpan: 2, margin: new Thickness(-4, 0, -4, 8));
                    PlayButton.Width = PlayButton.Height = 46;
                    Put(VolumeRow, 4, colSpan: 2, margin: new Thickness(-3, 0, 0, 0));
                    break;
                }
            }
            OnPlayerChanged();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateProgress);
        }

        // ---------- Bedienung ----------

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!Player.HasTrack && !Player.IsLocal && !SpotifyAuth.IsConnected) SpotifyPlayer.OpenSpotify();
            else Player.TogglePlay();
        }

        private void Next_Click(object sender, RoutedEventArgs e) => Player.Next();
        private void Prev_Click(object sender, RoutedEventArgs e) => Player.Previous();
        private void Shuffle_Click(object sender, RoutedEventArgs e) => Player.ToggleShuffle();
        private void Like_Click(object sender, RoutedEventArgs e) => Player.ToggleLike();
        private void OpenSpotify_Click(object sender, RoutedEventArgs e) => SpotifyPlayer.OpenSpotify();

        private void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // nicht die Kachel verschieben
            if (Player.Duration <= TimeSpan.Zero || ProgressTrack.ActualWidth <= 0) return;
            double pct = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
            Player.Seek(Player.Duration * pct);
            UpdateProgress();
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingVolume || !_playerAttached) return;
            Player.SetVolume((int)Math.Round(e.NewValue));
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (Player.Volume is not int v) return;
            if (v > 0)
            {
                _volumeBeforeMute = v;
                Player.SetVolume(0);
            }
            else Player.SetVolume(Math.Max(10, _volumeBeforeMute));
        }

        /// <summary>Mausrad über dem Player = Lautstärke.</summary>
        private bool PlayerWheel(MouseWheelEventArgs e)
        {
            if (!IsPlayer || Keyboard.Modifiers != ModifierKeys.None || Player.Volume is not int v) return false;
            Player.SetVolume(v + (e.Delta > 0 ? 5 : -5));
            return true;
        }

        // ---------- Menü: Spotify ----------

        private void CoverSwitch_Click(object sender, RoutedEventArgs e)
        {
            Note.CoverBackdrop = CoverSwitch.IsChecked == true;
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void SpotifyAccount_Click(object sender, RoutedEventArgs e)
        {
            SyncSpotifyPage();
            AccountStatus.Visibility = Visibility.Collapsed;
            MainPage.Visibility = Visibility.Collapsed;
            SpotifyPage.Visibility = Visibility.Visible;
            if (!SpotifyAuth.IsConnected) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ClientIdBox.Focus());
        }

        private void SyncSpotifyPage()
        {
            bool connected = SpotifyAuth.IsConnected;
            AccountConnected.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            AccountForm.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            AccountState.Text = connected ? "Verbunden" : "Optional";
            if (string.IsNullOrEmpty(ClientIdBox.Text) && SpotifyAuth.ClientId != null) ClientIdBox.Text = SpotifyAuth.ClientId;
        }

        private void ClientIdBox_TextChanged(object sender, TextChangedEventArgs e) =>
            ClientIdPlaceholder.Visibility = ClientIdBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void ClientIdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) SpotifyConnect_Click(sender, e);
        }

        private async void SpotifyConnect_Click(object sender, RoutedEventArgs e)
        {
            AccountStatus.Visibility = Visibility.Visible;
            AccountStatus.Text = "Browser geöffnet – bestätige dort den Zugriff …";
            try
            {
                await SpotifyAuth.ConnectAsync(ClientIdBox.Text);
                AccountStatus.Text = "Verbunden ✓";
                SyncSpotifyPage();
                App.ShowBalloon("Spotify verbunden", "Lieblingssongs, Zufall und andere Geräte sind jetzt im Player verfügbar.");
            }
            catch (Exception ex)
            {
                AccountStatus.Text = ex.Message;
                if (!MenuPopup.IsOpen) App.ShowBalloon("Spotify-Verbindung fehlgeschlagen", ex.Message);
            }
        }

        private void SpotifyDisconnect_Click(object sender, RoutedEventArgs e)
        {
            SpotifyAuth.Disconnect();
            SyncSpotifyPage();
        }

        private void SpotifyHelp_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ClientIdHelpUrl) { UseShellExecute = true });
        }
    }
}
