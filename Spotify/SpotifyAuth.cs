using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetNotes.Spotify
{
    /// <summary>
    /// Optionale Anmeldung bei Spotify (Authorization Code + PKCE über 127.0.0.1:8888).
    /// Tokens liegen DPAPI-verschlüsselt in %APPDATA%\WidgetNotes\spotify.dat.
    /// </summary>
    public static class SpotifyAuth
    {
        public const string RedirectUri = "http://127.0.0.1:8888/callback";
        private const int Port = 8888;
        private const string Scopes =
            "user-read-playback-state user-modify-playback-state user-read-currently-playing user-library-read user-library-modify";

        private static readonly string FilePath = Path.Combine(NoteStore.Folder, "spotify.dat");
        internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private sealed class Data
        {
            public string? ClientId { get; set; }
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private static Data _data = Load();
        private static Task<string?>? _refreshing;
        private static CancellationTokenSource? _pending;

        public static bool IsConnected => _data.RefreshToken != null && _data.ClientId != null;
        public static string? ClientId => _data.ClientId;

        public static event Action? Changed;

        private static Data Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Data();
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Data>(plain) ?? new Data();
            }
            catch
            {
                return new Data();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(NoteStore.Folder);
                var plain = JsonSerializer.SerializeToUtf8Bytes(_data);
                File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
            }
            catch { }
        }

        public static void Disconnect()
        {
            _data = new Data { ClientId = _data.ClientId };
            Save();
            Changed?.Invoke();
        }

        public static void CancelPending() => _pending?.Cancel();

        /// <summary>Öffnet den Browser, wartet auf die Bestätigung und speichert die Tokens.</summary>
        public static async Task ConnectAsync(string clientId)
        {
            clientId = clientId.Trim();
            if (clientId.Length != 32 || !IsHex(clientId))
                throw new ArgumentException("Das sieht nicht wie eine Client ID aus (32 Zeichen aus 0–9 und a–f).");

            _pending?.Cancel();
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _pending = cts;

            var verifier = B64Url(RandomNumberGenerator.GetBytes(64));
            var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = B64Url(RandomNumberGenerator.GetBytes(16));

            var url = "https://accounts.spotify.com/authorize?" + Query(new()
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = RedirectUri,
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = challenge,
                ["scope"] = Scopes,
                ["state"] = state,
            });

            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
            }
            catch (SocketException)
            {
                throw new InvalidOperationException($"Port {Port} ist belegt – läuft gerade eine andere Anmeldung?");
            }

            string code;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                code = await WaitForCode(listener, state, cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Anmeldung abgebrochen oder Zeit abgelaufen.");
            }
            finally
            {
                listener.Stop();
                if (_pending == cts) _pending = null;
            }

            var json = await TokenRequest(new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            });
            _data = new Data { ClientId = clientId };
            Apply(json);
            Changed?.Invoke();
        }

        private static async Task<string> WaitForCode(TcpListener listener, string expectedState, CancellationToken ct)
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                await using var stream = client.GetStream();
                var buffer = new byte[8192];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                    if (n == 0) break;
                    read += n;
                    if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n")) break;
                }
                var line = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
                var parts = line.Split(' ');
                var target = parts.Length > 1 ? parts[1] : "/";
                if (!target.StartsWith("/callback"))
                {
                    await Respond(stream, 404, "");
                    continue;
                }

                var q = ParseQuery(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");
                q.TryGetValue("code", out var code);
                q.TryGetValue("state", out var state);
                q.TryGetValue("error", out var error);
                bool ok = error == null && code != null && state == expectedState;
                await Respond(stream, 200, Page(ok, error));
                if (ok) return code!;
                throw new InvalidOperationException(error == "access_denied" ? "Zugriff wurde abgelehnt." : $"Anmeldung fehlgeschlagen ({error ?? "ungültige Antwort"}).");
            }
        }

        private static string Page(bool ok, string? error) =>
            "<!doctype html><meta charset=\"utf-8\"><title>Widget Notes</title>" +
            "<body style=\"margin:0;height:100vh;display:grid;place-items:center;background:#111214;color:#f2f2f5;font:16px 'Segoe UI',system-ui\">" +
            "<div style=\"text-align:center\"><div style=\"font-size:44px\">" + (ok ? "✓" : "✕") + "</div><p>" +
            (ok ? "Spotify ist verbunden. Du kannst diesen Tab schließen." : "Anmeldung fehlgeschlagen: " + WebUtility.HtmlEncode(error ?? "ungültige Antwort")) +
            "</p></div>";

        private static async Task Respond(NetworkStream stream, int status, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(body);
        }

        /// <summary>Gültiges Access-Token (erneuert es bei Bedarf) – oder null, wenn nicht verbunden.</summary>
        public static Task<string?> GetTokenAsync(bool force = false)
        {
            if (!IsConnected) return Task.FromResult<string?>(null);
            if (!force && _data.AccessToken != null && DateTime.UtcNow < _data.ExpiresAt) return Task.FromResult<string?>(_data.AccessToken);
            return _refreshing ??= Refresh();
        }

        private static async Task<string?> Refresh()
        {
            try
            {
                var json = await TokenRequest(new()
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _data.RefreshToken!,
                    ["client_id"] = _data.ClientId!,
                });
                Apply(json);
                return _data.AccessToken;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("invalid_grant") || ex.Message.Contains("revoked"))
            {
                Disconnect();
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                _refreshing = null;
            }
        }

        private static void Apply(JsonElement t)
        {
            _data.AccessToken = t.GetProperty("access_token").GetString();
            if (t.TryGetProperty("refresh_token", out var r) && r.GetString() is string refresh) _data.RefreshToken = refresh;
            _data.ExpiresAt = DateTime.UtcNow.AddSeconds(t.GetProperty("expires_in").GetInt32() - 60);
            Save();
        }

        private static async Task<JsonElement> TokenRequest(Dictionary<string, string> form)
        {
            using var res = await Http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form));
            var text = await res.Content.ReadAsStringAsync();
            JsonElement json;
            try { json = JsonDocument.Parse(text).RootElement.Clone(); }
            catch { json = default; }
            if (!res.IsSuccessStatusCode)
            {
                string? msg = null;
                if (json.ValueKind == JsonValueKind.Object)
                {
                    if (json.TryGetProperty("error_description", out var d)) msg = d.GetString();
                    if (json.TryGetProperty("error", out var e)) msg = $"{e.GetString()}: {msg}";
                }
                throw new InvalidOperationException(msg ?? $"Token-Fehler {(int)res.StatusCode}");
            }
            return json;
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Query(Dictionary<string, string> q)
        {
            var sb = new StringBuilder();
            foreach (var (k, v) in q)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>();
            foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = pair.IndexOf('=');
                var k = Uri.UnescapeDataString((i < 0 ? pair : pair[..i]).Replace('+', ' '));
                var v = i < 0 ? "" : Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
                d[k] = v;
            }
            return d;
        }
    }
}
