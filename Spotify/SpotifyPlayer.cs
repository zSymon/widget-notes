using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace WidgetNotes.Spotify
{
    /// <summary>
    /// Gemeinsamer Player-Zustand für alle Spotify-Kacheln.
    /// Quelle 1: die Spotify-App auf diesem PC über die Windows-Mediensteuerung (sofort, ohne Login).
    /// Quelle 2 (optional): die Spotify Web API – für Lieblingssongs, Zufall und Wiedergabe auf anderen Geräten.
    /// </summary>
    public sealed class SpotifyPlayer
    {
        public static SpotifyPlayer Instance { get; } = new();

        public event Action? Changed;

        // ---------- Anzeige ----------
        public string? Title { get; private set; }
        public string? Artist { get; private set; }
        public BitmapSource? Cover { get; private set; }
        public CoverPalette Palette { get; private set; } = CoverPalette.Default;
        public bool Playing { get; private set; }
        public TimeSpan Duration { get; private set; }
        public bool? Shuffle { get; private set; }
        public bool? Liked { get; private set; }
        public int? Volume { get; private set; }
        public bool CanSeek { get; private set; }
        /// <summary>Gerät, falls die Musik nicht auf diesem PC läuft (z. B. „iPhone“).</summary>
        public string? RemoteDevice { get; private set; }
        public bool HasTrack => Title != null;
        public bool IsLocal => _session != null;

        private TimeSpan _pos;
        private DateTime _posAt = DateTime.UtcNow;

        public TimeSpan Position
        {
            get
            {
                var p = Playing ? _pos + (DateTime.UtcNow - _posAt) : _pos;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }

        // ---------- Quellen ----------
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private Local? _local;
        private RemoteState? _remote;
        private string? _likedUri;
        private string? _coverKey;
        private int _users;
        private DispatcherTimer? _poll;
        private DispatcherTimer? _soon;
        private DateTime _holdUntil;

        private sealed record Local(string Title, string Artist, bool Playing, TimeSpan Position, DateTime PositionAt, TimeSpan Duration, bool? Shuffle, bool CanSeek);

        private static Dispatcher Ui => Application.Current.Dispatcher;

        private SpotifyPlayer()
        {
            SpotifyAuth.Changed += () => Ui.BeginInvoke(() =>
            {
                _remote = null;
                _likedUri = null;
                Liked = null;
                Compose();
                PollSoon(0);
            });
        }

        /// <summary>Kachel meldet sich an/ab – ohne Kacheln läuft nichts im Hintergrund.</summary>
        public void Attach()
        {
            if (_users++ == 0) Start();
            else Changed?.Invoke();
        }

        public void Detach()
        {
            if (_users == 0) return;
            if (--_users == 0) Stop();
        }

        private async void Start()
        {
            // Debug: WIDGETNOTES_SPOTIFY_MOCK=cover.png zeigt einen Beispielsong (für Screenshots)
            if (Environment.GetEnvironmentVariable("WIDGETNOTES_SPOTIFY_MOCK") is { Length: > 0 } mock)
            {
                Title = "Midnight City";
                Artist = "M83";
                Playing = true;
                Duration = TimeSpan.FromSeconds(243);
                _pos = TimeSpan.FromSeconds(97);
                Shuffle = true;
                Liked = true;
                Volume = 64;
                CanSeek = true;
                try { SetCover(Decode(File.OpenRead(mock)), mock); } catch { Changed?.Invoke(); }
                return;
            }

            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _poll.Tick += (_, _) => Poll();
            _poll.Start();
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.SessionsChanged += OnSessionsChanged;
                PickSession();
            }
            catch
            {
                // Mediensteuerung nicht verfügbar – dann nur über die Web API.
            }
            Poll();
        }

        private void Stop()
        {
            _poll?.Stop();
            _soon?.Stop();
            if (_manager != null) _manager.SessionsChanged -= OnSessionsChanged;
            SetSession(null);
            _manager = null;
        }

        // ---------- Spotify-App auf diesem PC (Windows-Mediensteuerung) ----------

        // Nur die offizielle App: Betterfy u. Ä. melden Windows keine Songinfos – die kommen dann über die Web API.
        private static bool IsSpotify(string? appId) =>
            appId != null && appId.Contains("spotify", StringComparison.OrdinalIgnoreCase);

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
            Ui.BeginInvoke(PickSession);

        private void PickSession()
        {
            if (_manager == null) return;
            GlobalSystemMediaTransportControlsSession? found = null;
            try { found = _manager.GetSessions().FirstOrDefault(s => IsSpotify(s.SourceAppUserModelId)); }
            catch { }
            if (found?.SourceAppUserModelId != _session?.SourceAppUserModelId || (found == null) != (_session == null))
                SetSession(found);
        }

        private void SetSession(GlobalSystemMediaTransportControlsSession? s)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }
            _session = s;
            _local = null;
            if (s != null)
            {
                s.MediaPropertiesChanged += OnMediaChanged;
                s.PlaybackInfoChanged += OnPlaybackChanged;
                s.TimelinePropertiesChanged += OnTimelineChanged;
                ReadLocal(withCover: true);
            }
            else Compose();
        }

        private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) =>
            Ui.BeginInvoke(() => ReadLocal(withCover: true));

        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) =>
            Ui.BeginInvoke(() => ReadLocal(withCover: false));

        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) =>
            Ui.BeginInvoke(() => ReadLocal(withCover: false));

        private async void ReadLocal(bool withCover)
        {
            var s = _session;
            if (s == null) return;
            try
            {
                var media = await s.TryGetMediaPropertiesAsync();
                if (s != _session) return;
                var info = s.GetPlaybackInfo();
                var tl = s.GetTimelineProperties();

                bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var title = media?.Title ?? "";
                var artist = media?.Artist ?? "";
                var duration = tl.EndTime - tl.StartTime;
                // Spotify meldet die Position nur bei Sprüngen – ab dem Zeitpunkt der Meldung hochrechnen.
                var at = tl.LastUpdatedTime.UtcDateTime;
                if (at > DateTime.UtcNow || at.Year < 2000) at = DateTime.UtcNow;
                bool? shuffle = info.Controls.IsShuffleEnabled ? info.IsShuffleActive : null;

                var previous = _local;
                _local = title.Length == 0 ? null : new Local(title, artist, playing, tl.Position - tl.StartTime, at, duration, shuffle, info.Controls.IsPlaybackPositionEnabled);

                bool trackChanged = previous?.Title != _local?.Title || previous?.Artist != _local?.Artist;
                if (trackChanged || (withCover && Cover == null))
                {
                    var key = $"{title}\n{artist}";
                    if (media?.Thumbnail != null) _ = LoadLocalCover(media.Thumbnail, key);
                    else if (trackChanged) Retry(() => ReadLocal(withCover: true), 600);
                }
                if (trackChanged) PollSoon(700); // Herz/Zufall zum neuen Song über die API nachladen
                Compose();
            }
            catch
            {
                // Sitzung gerade weg – nächstes Ereignis abwarten.
            }
        }

        private async Task LoadLocalCover(Windows.Storage.Streams.IRandomAccessStreamReference thumb, string key)
        {
            try
            {
                using var ras = await thumb.OpenReadAsync();
                using var src = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await src.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = Decode(ms);
                if (_local == null || $"{_local.Title}\n{_local.Artist}" != key) return;
                SetCover(bmp, key);
            }
            catch { }
        }

        // ---------- Web API (optional) ----------

        private bool _polling;

        private async void Poll()
        {
            if (_users == 0 || _polling) return;
            // Läuft Spotify auf diesem PC, kommt die Lautstärke direkt aus dem Windows-Mixer.
            if (_session != null && DateTime.UtcNow > _holdUntil)
            {
                var v = AppVolume.Get();
                if (v != Volume)
                {
                    Volume = v;
                    Changed?.Invoke();
                }
            }
            if (!SpotifyAuth.IsConnected)
            {
                SetPollInterval(3);
                return;
            }

            _polling = true;
            try
            {
                var r = await SpotifyApi.PlaybackAsync();
                if (DateTime.UtcNow < _holdUntil) return;
                _remote = r;
                if (r?.Uri != null && r.Uri != _likedUri)
                {
                    _likedUri = r.Uri;
                    Liked = null;
                    var saved = await SpotifyApi.IsSaved(r.Uri);
                    if (_likedUri == r.Uri) Liked = saved;
                }
                Compose();
            }
            finally
            {
                _polling = false;
                // Lokal läuft die Anzeige über Ereignisse – die API nur ab und zu fragen.
                SetPollInterval(_local != null ? 3 : _remote?.Playing == true ? 2 : 4);
                if (_local == null && _remote is { Playing: true } rs && rs.Duration > TimeSpan.Zero)
                {
                    var left = rs.Duration - rs.Position;
                    if (left < TimeSpan.FromSeconds(2.5)) PollSoon((int)Math.Max(300, left.TotalMilliseconds + 350));
                }
            }
        }

        private void SetPollInterval(double seconds)
        {
            if (_poll != null && Math.Abs(_poll.Interval.TotalSeconds - seconds) > 0.01) _poll.Interval = TimeSpan.FromSeconds(seconds);
        }

        private void PollSoon(int ms)
        {
            _soon ??= new DispatcherTimer();
            _soon.Stop();
            _soon.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            _soon.Tick -= SoonTick;
            _soon.Tick += SoonTick;
            _soon.Start();
        }

        private void SoonTick(object? sender, EventArgs e)
        {
            _soon?.Stop();
            Poll();
        }

        // ---------- Zusammenführen ----------

        private void Compose()
        {
            var r = _remote;
            if (_local is Local l)
            {
                Title = l.Title;
                Artist = l.Artist;
                Playing = l.Playing;
                _pos = l.Position;
                _posAt = l.PositionAt;
                Duration = l.Duration;
                CanSeek = l.CanSeek || SpotifyAuth.IsConnected;
                bool sameTrack = r?.Title != null && string.Equals(r.Title, l.Title, StringComparison.OrdinalIgnoreCase);
                Shuffle = r != null ? r.Shuffle : l.Shuffle;
                if (!sameTrack) Liked = null;
                else if (_likedUri != r!.Uri) Liked = null;
                RemoteDevice = null;
            }
            else if (r?.Title != null)
            {
                Title = r.Title;
                Artist = r.Artist;
                Playing = r.Playing;
                _pos = r.Position;
                _posAt = DateTime.UtcNow;
                Duration = r.Duration;
                CanSeek = true;
                Shuffle = r.Shuffle;
                Volume = r.Volume;
                RemoteDevice = r.DeviceName;
                if (r.CoverUrl != null && r.CoverUrl != _coverKey) _ = LoadRemoteCover(r.CoverUrl);
            }
            else
            {
                Title = null;
                Artist = null;
                Playing = false;
                _pos = TimeSpan.Zero;
                Duration = TimeSpan.Zero;
                Shuffle = null;
                Liked = null;
                RemoteDevice = null;
                SetCover(null, null);
                return;
            }
            Changed?.Invoke();
        }

        private async Task LoadRemoteCover(string url)
        {
            _coverKey = url;
            try
            {
                var bytes = await SpotifyAuth.Http.GetByteArrayAsync(url);
                if (_coverKey != url || _local != null) return;
                SetCover(Decode(new MemoryStream(bytes)), url);
            }
            catch { }
        }

        private void SetCover(BitmapSource? bmp, string? key)
        {
            _coverKey = key;
            Cover = bmp;
            Palette = CoverPalette.From(bmp);
            Changed?.Invoke();
        }

        private static BitmapSource Decode(Stream s)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = s;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static void Retry(Action a, int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); a(); };
            t.Start();
        }

        // ---------- Befehle ----------

        /// <summary>Kurz eigene Annahme anzeigen, bevor die API den neuen Stand meldet.</summary>
        private void Hold(int ms = 1500) => _holdUntil = DateTime.UtcNow.AddMilliseconds(ms);

        public async void TogglePlay()
        {
            if (_session != null)
            {
                try { await _session.TryTogglePlayPauseAsync(); } catch { }
                return;
            }
            if (!SpotifyAuth.IsConnected) return;
            bool play = !Playing;
            _pos = Position;
            _posAt = DateTime.UtcNow;
            Playing = play;
            Hold();
            Changed?.Invoke();
            await (play ? SpotifyApi.Play() : SpotifyApi.Pause());
            PollSoon(600);
        }

        public async void Next()
        {
            if (_session != null) { try { await _session.TrySkipNextAsync(); } catch { } return; }
            await SpotifyApi.Next();
            PollSoon(400);
        }

        public async void Previous()
        {
            if (_session != null) { try { await _session.TrySkipPreviousAsync(); } catch { } return; }
            await SpotifyApi.Previous();
            PollSoon(400);
        }

        public async void ToggleShuffle()
        {
            if (Shuffle is not bool current) return;
            Shuffle = !current;
            Hold();
            Changed?.Invoke();
            if (SpotifyAuth.IsConnected)
            {
                await SpotifyApi.Shuffle(!current);
                PollSoon(600);
            }
            else if (_session != null)
            {
                try { await _session.TryChangeShuffleActiveAsync(!current); } catch { }
            }
        }

        public async void ToggleLike()
        {
            if (Liked is not bool current || _likedUri is not string uri) return;
            Liked = !current;
            Changed?.Invoke();
            bool ok = await (current ? SpotifyApi.Remove(uri) : SpotifyApi.Save(uri));
            if (!ok && _likedUri == uri)
            {
                Liked = current;
                Changed?.Invoke();
            }
        }

        private DispatcherTimer? _volumeTimer;

        public void SetVolume(int pct)
        {
            pct = Math.Clamp(pct, 0, 100);
            Volume = pct;
            Hold(2000);
            Changed?.Invoke();
            // Beim Ziehen nicht jeden Schritt senden
            _volumeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _volumeTimer.Stop();
            _volumeTimer.Tick -= VolumeTick;
            _volumeTimer.Tick += VolumeTick;
            _volumeTimer.Start();
        }

        private async void VolumeTick(object? sender, EventArgs e)
        {
            _volumeTimer?.Stop();
            if (Volume is not int pct) return;
            // Läuft die Musik auf diesem PC, direkt im Mixer – das klappt auch ohne Konto und ohne Premium-Fehler.
            if (_session != null && AppVolume.Set(pct)) return;
            if (SpotifyAuth.IsConnected) await SpotifyApi.Volume(pct);
        }

        public async void Seek(TimeSpan pos)
        {
            if (Duration <= TimeSpan.Zero) return;
            _pos = pos;
            _posAt = DateTime.UtcNow;
            Hold();
            Changed?.Invoke();
            if (_session != null && _local?.CanSeek == true)
            {
                try { if (await _session.TryChangePlaybackPositionAsync(pos.Ticks)) return; } catch { }
            }
            if (SpotifyAuth.IsConnected)
            {
                await SpotifyApi.Seek(pos);
                PollSoon(600);
            }
        }

        public static void OpenSpotify()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("spotify:") { UseShellExecute = true }); }
            catch
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://open.spotify.com") { UseShellExecute = true });
            }
        }
    }
}
