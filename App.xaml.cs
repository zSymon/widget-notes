using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WidgetNotes
{
    public partial class App : Application
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "WidgetNotes";

        private const string ShowEventName = "WidgetNotes_ShowWidgets";
        private const string AutostartArg = "--autostart";
        private const string UpdatedArg = "--updated";
        private const string InstallUpdateArg = "--install-update";

        private Mutex? _mutex;
        private EventWaitHandle? _showEvent;
        private Forms.NotifyIcon? _tray;
        private DispatcherTimer? _clock;
        private readonly List<NoteWindow> _windows = new();

        private DispatcherTimer? _relayoutTimer;
        private SnapPreview? _preview;

        public bool IsExiting { get; private set; }

        /// <summary>Vorschau beim Ziehen (wird neu erzeugt, falls ein Explorer-Neustart sie zerstört hat).</summary>
        public SnapPreview Preview
        {
            get
            {
                if (_preview == null || _preview.IsClosed) _preview = new SnapPreview();
                return _preview;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                ReportError(args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex) LogError(ex);
            };

            bool isUpdated = e.Args.Contains(UpdatedArg);

            _mutex = new Mutex(true, "WidgetNotes_SingleInstance", out bool isFirstInstance);
            if (!isFirstInstance && isUpdated)
            {
                // Nach einem Update: warten, bis sich die alte Version beendet hat.
                try { isFirstInstance = _mutex.WaitOne(TimeSpan.FromSeconds(20)); }
                catch (AbandonedMutexException) { isFirstInstance = true; }
            }
            if (!isFirstInstance)
            {
                // Läuft schon: die laufende Instanz soll die Kacheln zeigen.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(new Action(ToggleDesktop)), null, Timeout.Infinite, executeOnlyOnce: false);

            bool isAutostart = e.Args.Contains(AutostartArg);
            bool installNow = e.Args.Contains(InstallUpdateArg);
            bool isFirstRun = !System.IO.File.Exists(NoteStore.FilePath);

            // Beim Hochfahren ist der Desktop evtl. noch nicht da – kurz warten, sonst landen die
            // Kacheln nicht auf dem Desktop bzw. an falschen Stellen.
            if (isAutostart) WaitForDesktop(TimeSpan.FromSeconds(60));

            NoteStore.Load();
            if (NoteStore.Notes.Count == 0)
                NoteStore.Notes.Add(new NoteData { Title = "Willkommen 👋", Text = "Zieh mich an eine andere Stelle – ich raste im Raster ein.\nÜber ··· änderst du Größe und Design." });

            foreach (var note in NoteStore.Notes.ToList())
                OpenWindow(note, desired: null);

            NoteStore.SaveNow();
            SetupTray();
            StartClock();
            WatchDisplays();
            StartUpdateChecks(installNow);

            if (isUpdated)
            {
                ShowBalloon($"Aktualisiert auf v{Updater.Current}", "Widget Notes ist jetzt auf dem neuesten Stand.");
                var cleanup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                cleanup.Tick += (_, _) => { cleanup.Stop(); Updater.CleanupOld(); };
                cleanup.Start();
            }
            else Updater.CleanupOld();

            // Nach dem Hochfahren können Taskleiste/zweiter Bildschirm noch nachkommen.
            if (isAutostart)
            {
                var late = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                late.Tick += (_, _) => { late.Stop(); Relayout(); };
                late.Start();
            }

            // Kacheln liegen unter allen Fenstern – beim manuellen Start den Desktop zeigen,
            // damit man sieht, dass die App läuft.
            if (!isAutostart && !isUpdated && !installNow)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ToggleDesktop));
                if (isFirstRun)
                    ShowBalloon("Widget Notes läuft",
                        "Deine Notizen liegen als Kacheln auf dem Desktop. Ein Klick auf das Symbol hier unten zeigt sie jederzeit.");
            }
        }

        // ---------- Updates ----------

        public UpdateInfo? AvailableUpdate { get; private set; }
        private bool _updating;
        private Version? _notifiedVersion;
        private DispatcherTimer? _updateTimer;
        private Forms.ToolStripMenuItem? _updateItem;

        private void StartUpdateChecks(bool installNow)
        {
            if (installNow)
            {
                InstallUpdate();
                return;
            }
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _updateTimer.Tick += (_, _) =>
            {
                _updateTimer.Interval = TimeSpan.FromHours(6);
                CheckForUpdates(manual: false);
            };
            _updateTimer.Start();
        }

        public async void CheckForUpdates(bool manual)
        {
            try
            {
                AvailableUpdate = await Updater.CheckAsync();
                RefreshUpdateItem();
                if (AvailableUpdate is UpdateInfo info)
                {
                    if (manual || _notifiedVersion != info.Version)
                    {
                        _notifiedVersion = info.Version;
                        ShowBalloon($"Update verfügbar: v{info.Version}", "Hier klicken, um es jetzt zu installieren.", InstallUpdate);
                    }
                }
                else if (manual)
                {
                    ShowBalloon("Alles aktuell", $"Du hast die neueste Version (v{Updater.Current}).");
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                if (manual) ShowBalloon("Update-Prüfung fehlgeschlagen", ex.Message);
            }
        }

        public async void InstallUpdate()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var info = AvailableUpdate ?? await Updater.CheckAsync();
                if (info == null)
                {
                    ShowBalloon("Alles aktuell", $"Du hast die neueste Version (v{Updater.Current}).");
                    return;
                }

                ShowBalloon($"Update auf v{info.Version}", "Wird heruntergeladen …");
                var downloaded = await Updater.DownloadAsync(info);

                NoteStore.SaveNow();
                Updater.SwapExe(downloaded);

                // Instanz-Sperre freigeben, damit die neue Version starten kann.
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
                _mutex = null;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, UpdatedArg)
                {
                    UseShellExecute = false,
                });
                ExitApp();
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show($"Das Update konnte nicht installiert werden:\n\n{ex.Message}", "Widget Notes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _updating = false;
            }
        }

        private void RefreshUpdateItem()
        {
            if (_updateItem == null) return;
            _updateItem.Text = AvailableUpdate is UpdateInfo info ? $"Update auf v{info.Version} installieren" : "Nach Updates suchen";
            _updateItem.Font = new Drawing.Font(_updateItem.Font, AvailableUpdate != null ? Drawing.FontStyle.Bold : Drawing.FontStyle.Regular);
        }

        private Action? _balloonAction;

        /// <summary>Benachrichtigung zeigen; ein Klick darauf führt <paramref name="onClick"/> aus (Standard: Desktop zeigen).</summary>
        private void ShowBalloon(string title, string text, Action? onClick = null)
        {
            if (_tray == null) return;
            _balloonAction = onClick ?? ToggleDesktop;
            _tray.ShowBalloonTip(8000, title, text, Forms.ToolTipIcon.None);
        }

        // ---------- Fehler ----------

        private static readonly string LogPath = System.IO.Path.Combine(NoteStore.Folder, "error.log");

        private static void LogError(Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(NoteStore.Folder);
                System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        private static void ReportError(Exception ex)
        {
            LogError(ex);
            MessageBox.Show($"Da ist etwas schiefgelaufen:\n\n{ex.Message}\n\nDetails stehen in:\n{LogPath}",
                "Widget Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public IEnumerable<Rect> OccupiedExcept(NoteWindow self) =>
            _windows.Where(w => w != self && w.IsPlaced).Select(w => w.TileRect);

        private NoteWindow OpenWindow(NoteData note, Point? desired)
        {
            var w = new NoteWindow(note);
            _windows.Add(w);
            if (desired is Point p) w.PlaceAt(p, animate: false);
            else w.RestoreHome(animate: false);
            w.Show();
            return w;
        }

        // ---------- Bildschirme & Explorer ----------

        private static void WaitForDesktop(TimeSpan timeout)
        {
            var until = DateTime.Now + timeout;
            while (NativeMethods.FindWindow("Progman", null) == IntPtr.Zero && DateTime.Now < until)
                Thread.Sleep(500);
        }

        private void WatchDisplays()
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnDisplaysChanged(object? sender, EventArgs e) => RelayoutSoon();

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            // Taskleiste verschoben/Größe geändert → Arbeitsbereich anders.
            if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General) RelayoutSoon();
        }

        /// <summary>Kurz warten, bis sich die Bildschirme eingependelt haben, dann neu anordnen.</summary>
        private void RelayoutSoon()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_relayoutTimer == null)
                {
                    _relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    _relayoutTimer.Tick += (_, _) => { _relayoutTimer.Stop(); Relayout(); };
                }
                _relayoutTimer.Stop();
                _relayoutTimer.Start();
            });
        }

        /// <summary>Alle Kacheln an ihr gespeichertes Zuhause setzen (fehlende Bildschirme → Hauptbildschirm).</summary>
        private void Relayout()
        {
            if (IsExiting) return;
            foreach (var w in _windows) w.Unplace();
            foreach (var w in _windows) w.RestoreHome(animate: true);
        }

        /// <summary>Kachel wurde von Windows zerstört (Explorer-Neustart) – neu erstellen, sobald der Desktop zurück ist.</summary>
        public void OnWindowLost(NoteWindow window)
        {
            _windows.Remove(window);
            var note = window.Note;
            var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            int attempts = 0;
            retry.Tick += (_, _) =>
            {
                attempts++;
                if (IsExiting || !NoteStore.Notes.Contains(note)) { retry.Stop(); return; }
                if (NativeMethods.FindWindow("Progman", null) == IntPtr.Zero && attempts < 60) return;
                retry.Stop();
                OpenWindow(note, desired: null);
            };
            retry.Start();
        }

        public void CreateNote(NoteWindow? near = null)
        {
            var note = new NoteData { Theme = near?.Note.Theme ?? Themes.DefaultKey };
            Point desired;
            if (near != null)
            {
                var r = near.TileRect;
                desired = new Point(r.X - GridLayout.TileSize(note.Units).Width - GridLayout.Gap, r.Y);
            }
            else desired = GridLayout.DefaultSpot(note.Units);

            NoteStore.Notes.Add(note);
            var w = OpenWindow(note, desired);
            w.FocusEditor();
        }

        public void ApplyStyleToAll(NoteData source)
        {
            foreach (var w in _windows)
            {
                if (w.Note == source) continue;
                w.Note.CopyStyleFrom(source);
                w.ApplyStyle();
            }
            NoteStore.SaveSoon();
        }

        // ---------- Uhr: Fälligkeiten aktualisieren & erinnern ----------

        private void StartClock()
        {
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _clock.Tick += (_, _) => ClockTick();
            _clock.Start();
            ClockTick();
        }

        private void ClockTick()
        {
            foreach (var w in _windows) w.RefreshTimeBased();

            var now = DateTime.Now;
            var due = new List<(NoteData Note, TodoItem Item)>();
            foreach (var note in NoteStore.Notes)
            {
                foreach (var item in note.Items)
                {
                    if (item.Done || item.Notified || item.ReminderTime is not DateTime when || when > now) continue;
                    item.Notified = true;
                    // Was schon lange vorbei ist (App war aus), nicht mehr melden.
                    if (now - when < TimeSpan.FromHours(12)) due.Add((note, item));
                }
            }
            if (due.Count == 0) return;

            NoteStore.SaveSoon();
            if (_tray == null) return;
            if (due.Count == 1)
            {
                var (note, item) = due[0];
                var where = string.IsNullOrWhiteSpace(note.Title) ? "Aufgabe fällig" : $"Fällig · {note.Title}";
                ShowBalloon(where, string.IsNullOrWhiteSpace(item.Text) ? "(ohne Text)" : item.Text);
            }
            else
            {
                var lines = string.Join("\n", due.Take(4).Select(d => "• " + d.Item.Text));
                ShowBalloon($"{due.Count} Aufgaben fällig", lines);
            }
        }

        public void DeleteNote(NoteWindow window)
        {
            NoteStore.Notes.Remove(window.Note);
            _windows.Remove(window);
            window.ForceClose();
            NoteStore.SaveNow();
        }

        /// <summary>Wie Win+D: blendet alle Fenster aus, die Kacheln bleiben sichtbar.</summary>
        private static void ToggleDesktop()
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return;
                dynamic shell = Activator.CreateInstance(type)!;
                shell.ToggleDesktop();
            }
            catch { }
        }

        private void ExitApp()
        {
            IsExiting = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            NoteStore.SaveNow();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            Shutdown();
        }

        // ---------- Tray ----------

        private void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Neue Notiz", null, (_, _) => CreateNote());
            menu.Items.Add("Desktop anzeigen", null, (_, _) => ToggleDesktop());
            menu.Items.Add(new Forms.ToolStripSeparator());
            var autostart = new Forms.ToolStripMenuItem("Mit Windows starten") { Checked = IsAutostartEnabled() };
            autostart.Click += (_, _) =>
            {
                SetAutostart(!autostart.Checked);
                autostart.Checked = IsAutostartEnabled();
            };
            menu.Items.Add(autostart);
            menu.Opening += (_, _) => autostart.Checked = IsAutostartEnabled();
            menu.Items.Add("Speicherordner öffnen", null, (_, _) =>
            {
                System.IO.Directory.CreateDirectory(NoteStore.Folder);
                System.Diagnostics.Process.Start("explorer.exe", NoteStore.Folder);
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            _updateItem = new Forms.ToolStripMenuItem("Nach Updates suchen");
            _updateItem.Click += (_, _) =>
            {
                if (AvailableUpdate != null) InstallUpdate();
                else CheckForUpdates(manual: true);
            };
            menu.Items.Add(_updateItem);
            menu.Items.Add(new Forms.ToolStripMenuItem($"Version {Updater.Current}") { Enabled = false });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Beenden", null, (_, _) => ExitApp());

            _tray = new Forms.NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Text = "Widget Notes – Klick zeigt den Desktop",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.BalloonTipClicked += (_, _) => (_balloonAction ?? ToggleDesktop)();
            _tray.MouseClick += (_, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) ToggleDesktop();
            };
        }

        private static Drawing.Icon CreateTrayIcon()
        {
            try
            {
                if (Environment.ProcessPath is string exe && Drawing.Icon.ExtractAssociatedIcon(exe) is Drawing.Icon appIcon)
                    return appIcon;
            }
            catch { }

            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using var path = new Drawing.Drawing2D.GraphicsPath();
                const int r = 9;
                path.AddArc(2, 2, r * 2, r * 2, 180, 90);
                path.AddArc(30 - r * 2, 2, r * 2, r * 2, 270, 90);
                path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(2, 30 - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                using var fill = new Drawing.Drawing2D.LinearGradientBrush(
                    new Drawing.Point(0, 0), new Drawing.Point(32, 32),
                    Drawing.Color.FromArgb(70, 70, 78), Drawing.Color.FromArgb(28, 28, 32));
                g.FillPath(fill, path);
                using var accent = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 214, 10));
                g.FillRectangle(accent, 8, 9, 10, 3);
                using var line = new Drawing.SolidBrush(Drawing.Color.FromArgb(220, 255, 255, 255));
                g.FillRectangle(line, 8, 15, 16, 2);
                g.FillRectangle(line, 8, 20, 12, 2);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        // ---------- Autostart (HKCU\...\Run) ----------

        public static bool IsAutostartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }

        public static void SetAutostart(bool enable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\" {AutostartArg}");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
