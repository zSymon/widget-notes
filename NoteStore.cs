using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace WidgetNotes
{
    /// <summary>Lädt und speichert alle Notizen als JSON in %APPDATA%\WidgetNotes\notes.json.</summary>
    public static class NoteStore
    {
        /// <summary>Für Tests per WIDGETNOTES_DATA umlenkbar (eigene Daten, eigene Instanz).</summary>
        public static readonly string Folder = Environment.GetEnvironmentVariable("WIDGETNOTES_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WidgetNotes");

        public static readonly string FilePath = Path.Combine(Folder, "notes.json");

        public static List<NoteData> Notes { get; private set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static DispatcherTimer? _saveTimer;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Notes = JsonSerializer.Deserialize<List<NoteData>>(json) ?? new();
                }
            }
            catch (Exception)
            {
                // Kaputte Datei nicht überschreiben, sondern sichern.
                try { File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); } catch { }
                Notes = new();
            }
        }

        /// <summary>Speichert verzögert (fasst schnelle Änderungen zusammen).</summary>
        public static void SaveSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public static void SaveNow()
        {
            _saveTimer?.Stop();
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Notes, JsonOptions));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception)
            {
                // Beim nächsten Speichern erneut versuchen.
            }
        }
    }
}
