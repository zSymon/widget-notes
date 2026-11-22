using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace WidgetNotes.Spotify
{
    /// <summary>Was die Web API über die aktuelle Wiedergabe meldet (auch Handy, Lautsprecher …).</summary>
    public sealed record RemoteState(
        string? Uri, string? Title, string? Artist, string? CoverUrl,
        bool Playing, TimeSpan Position, TimeSpan Duration,
        bool Shuffle, int? Volume, string? DeviceName);

    /// <summary>Schlanker Client für die paar Endpunkte, die der Player braucht.</summary>
    public static class SpotifyApi
    {
        private const string Base = "https://api.spotify.com/v1";
        private static DateTime _blockedUntil;

        private static async Task<HttpResponseMessage?> Send(HttpMethod method, string path, bool retry = true)
        {
            if (DateTime.UtcNow < _blockedUntil) return null;
            var token = await SpotifyAuth.GetTokenAsync();
            if (token == null) return null;

            using var req = new HttpRequestMessage(method, Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (method != HttpMethod.Get) req.Content = new ByteArrayContent(Array.Empty<byte>());
            HttpResponseMessage res;
            try { res = await SpotifyAuth.Http.SendAsync(req); }
            catch { return null; }

            if (res.StatusCode == HttpStatusCode.Unauthorized && retry)
            {
                res.Dispose();
                await SpotifyAuth.GetTokenAsync(force: true);
                return await Send(method, path, retry: false);
            }
            if ((int)res.StatusCode == 429)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _blockedUntil = DateTime.UtcNow + wait;
            }
            return res;
        }

        private static async Task<bool> Command(HttpMethod method, string path)
        {
            using var res = await Send(method, path);
            return res?.IsSuccessStatusCode == true;
        }

        public static async Task<RemoteState?> PlaybackAsync()
        {
            using var res = await Send(HttpMethod.Get, "/me/player?additional_types=episode");
            if (res == null || res.StatusCode == HttpStatusCode.NoContent || !res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var r = doc.RootElement;

            int? volume = null;
            string? device = null;
            if (r.TryGetProperty("device", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                if (d.TryGetProperty("volume_percent", out var v) && v.ValueKind == JsonValueKind.Number) volume = v.GetInt32();
                device = d.TryGetProperty("name", out var n) ? n.GetString() : null;
            }

            string? uri = null, title = null, artist = null, cover = null;
            var duration = TimeSpan.Zero;
            if (r.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            {
                uri = Str(item, "uri");
                title = Str(item, "name");
                duration = TimeSpan.FromMilliseconds(item.TryGetProperty("duration_ms", out var dm) ? dm.GetDouble() : 0);
                if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                    artist = string.Join(", ", artists.EnumerateArray().Select(a => Str(a, "name")).Where(s => s != null));
                JsonElement imagesOwner = default;
                if (item.TryGetProperty("album", out var album)) imagesOwner = album;
                else if (item.TryGetProperty("show", out var show))
                {
                    imagesOwner = show;
                    artist ??= Str(show, "name");
                }
                if (imagesOwner.ValueKind == JsonValueKind.Object && imagesOwner.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
                {
                    // Größe um 300 px: scharf genug, aber kein Riesen-Download
                    var list = imgs.EnumerateArray().Select(i => (Url: Str(i, "url"), W: i.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : 0)).ToList();
                    cover = list.Where(i => i.W >= 250).OrderBy(i => i.W).Select(i => i.Url).FirstOrDefault() ?? list.Select(i => i.Url).FirstOrDefault();
                }
            }

            return new RemoteState(uri, title, artist, cover,
                r.TryGetProperty("is_playing", out var p) && p.GetBoolean(),
                TimeSpan.FromMilliseconds(r.TryGetProperty("progress_ms", out var pm) && pm.ValueKind == JsonValueKind.Number ? pm.GetDouble() : 0),
                duration,
                r.TryGetProperty("shuffle_state", out var sh) && sh.ValueKind == JsonValueKind.True,
                volume, device);
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public static Task<bool> Play() => Command(HttpMethod.Put, "/me/player/play");
        public static Task<bool> Pause() => Command(HttpMethod.Put, "/me/player/pause");
        public static Task<bool> Next() => Command(HttpMethod.Post, "/me/player/next");
        public static Task<bool> Previous() => Command(HttpMethod.Post, "/me/player/previous");
        public static Task<bool> Shuffle(bool on) => Command(HttpMethod.Put, $"/me/player/shuffle?state={(on ? "true" : "false")}");
        public static Task<bool> Volume(int pct) => Command(HttpMethod.Put, $"/me/player/volume?volume_percent={Math.Clamp(pct, 0, 100)}");
        public static Task<bool> Seek(TimeSpan pos) => Command(HttpMethod.Put, $"/me/player/seek?position_ms={(long)pos.TotalMilliseconds}");

        // Seit 2026 gibt es für Lieblingssongs nur noch die allgemeinen /me/library-Endpunkte.
        public static Task<bool> Save(string uri) => Command(HttpMethod.Put, "/me/library?uris=" + Uri.EscapeDataString(uri));
        public static Task<bool> Remove(string uri) => Command(HttpMethod.Delete, "/me/library?uris=" + Uri.EscapeDataString(uri));

        public static async Task<bool?> IsSaved(string uri)
        {
            using var res = await Send(HttpMethod.Get, "/me/library/contains?uris=" + Uri.EscapeDataString(uri));
            if (res == null || !res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 && doc.RootElement[0].GetBoolean();
        }
    }
}
