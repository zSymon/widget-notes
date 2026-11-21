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

        private Mutex? _mutex;
        private Forms.NotifyIcon? _tray;
        private DispatcherTimer? _clock;
        private readonly List<NoteWindow> _windows = new();

        public bool IsExiting { get; private set; }
        public SnapPreview Preview { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, "WidgetNotes_SingleInstance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }

            Preview = new SnapPreview();

            NoteStore.Load();
            if (NoteStore.Notes.Count == 0)
                NoteStore.Notes.Add(new NoteData { Title = "Willkommen 👋", Text = "Zieh mich an eine andere Stelle – ich raste im Raster ein.\nÜber ··· änderst du Größe und Design." });

            foreach (var note in NoteStore.Notes.ToList())
                OpenWindow(note, desired: null);

            NoteStore.SaveNow();
            SetupTray();
            StartClock();
        }

        public IEnumerable<Rect> OccupiedExcept(NoteWindow self) =>
            _windows.Where(w => w != self && w.Note.X.HasValue).Select(w => w.TileRect);

        private NoteWindow OpenWindow(NoteData note, Point? desired)
        {
            var w = new NoteWindow(note);
            _windows.Add(w);
            if (desired is Point p) w.PlaceAt(p, animate: false);
            else w.PlaceInitial();
            w.Show();
            return w;
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

        private static bool IsAutostartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }

        private static void SetAutostart(bool enable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
