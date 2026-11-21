using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace WidgetNotes
{
    public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size);

    /// <summary>Holt neue Versionen aus den GitHub-Releases und tauscht die laufende Exe aus.</summary>
    public static class Updater
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/zSymon/widget-notes/releases/latest";
        private const string AssetName = "WidgetNotes.exe";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetNotes-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static Version Current
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return Normalize(v);
            }
        }

        private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

        /// <summary>Neueres Release mit WidgetNotes.exe – oder null, wenn die App aktuell ist.</summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi));
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = Normalize(version);
            if (version <= Current) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                return new UpdateInfo(version,
                    asset.GetProperty("browser_download_url").GetString()!,
                    asset.GetProperty("size").GetInt64());
            }
            return null;
        }

        /// <summary>Lädt die neue Exe in den Temp-Ordner und prüft sie grob.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info)
        {
            var target = Path.Combine(Path.GetTempPath(), $"WidgetNotes-{info.Version}.exe");
            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(target);
                await source.CopyToAsync(file);
            }

            var length = new FileInfo(target).Length;
            if (info.Size > 0 && length != info.Size)
                throw new IOException($"Download unvollständig ({length} von {info.Size} Bytes).");

            using (var check = File.OpenRead(target))
            {
                if (check.ReadByte() != 'M' || check.ReadByte() != 'Z')
                    throw new IOException("Die heruntergeladene Datei ist keine gültige Programmdatei.");
            }
            return target;
        }

        /// <summary>
        /// Ersetzt die laufende Exe. Unter Windows darf man eine laufende Exe umbenennen, nur nicht überschreiben –
        /// also: alte zur Seite schieben, neue an ihren Platz.
        /// </summary>
        public static void SwapExe(string newExe)
        {
            var current = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt.");
            var old = current + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try
            {
                File.Move(newExe, current);
            }
            catch
            {
                File.Move(old, current); // zurückrollen
                throw;
            }
        }

        /// <summary>Überbleibsel des letzten Updates löschen (klappt erst, wenn die alte Version beendet ist).</summary>
        public static void CleanupOld()
        {
            try
            {
                var old = Environment.ProcessPath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }
    }
}
