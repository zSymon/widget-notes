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

            _mutex = new Mutex(true, "WidgetNotes_SingleInstance", out bool isFirstInstance);
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

            // Nach dem Hochfahren können Taskleiste/zweiter Bildschirm noch nachkommen.
            if (isAutostart)
            {
                var late = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                late.Tick += (_, _) => { late.Stop(); Relayout(); };
                late.Start();
            }

            // Kacheln liegen unter allen Fenstern – beim manuellen Start den Desktop zeigen,
            // damit man sieht, dass die App läuft.
            if (!isAutostart)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ToggleDesktop));
                if (isFirstRun)
                    _tray?.ShowBalloonTip(8000, "Widget Notes läuft",
                        "Deine Notizen liegen als Kacheln auf dem Desktop. Ein Klick auf das Symbol hier unten zeigt sie jederzeit.",
                        Forms.ToolTipIcon.None);
            }
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
                desired = new Point(r.X - GridLayout.TileSize(note.Size).Width - GridLayout.Gap, r.Y);
            }
            else desired = GridLayout.DefaultSpot(note.Size);

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
                _tray.ShowBalloonTip(10000, where, string.IsNullOrWhiteSpace(item.Text) ? "(ohne Text)" : item.Text, Forms.ToolTipIcon.None);
            }
            else
            {
                var lines = string.Join("\n", due.Take(4).Select(d => "• " + d.Item.Text));
                _tray.ShowBalloonTip(10000, $"{due.Count} Aufgaben fällig", lines, Forms.ToolTipIcon.None);
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
            menu.Items.Add("Beenden", null, (_, _) => ExitApp());

            _tray = new Forms.NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Text = "Widget Notes – Klick zeigt den Desktop",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.BalloonTipClicked += (_, _) => ToggleDesktop();
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
