using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace EnglishBot.Speech;

/// <summary>
/// Turns an English word or sentence into a Telegram voice note.
///
/// Backed by Azure Speech, which can return OGG/Opus directly — exactly the format
/// Telegram wants for voice notes, so there is no ffmpeg step. Optional: with no key
/// configured, <see cref="Enabled"/> is false and the bot simply runs without audio.
///
/// Every clip is cached on disk, so each word costs one synthesis ever.
/// </summary>
public sealed class Tts : IDisposable
{
    private readonly HttpClient? _http;
    private readonly string _cacheDir;
    private readonly string _voice;
    private readonly string _region;
    private readonly string _key;

    public bool Enabled { get; }

    public Tts(BotConfig cfg)
    {
        _cacheDir = cfg.AudioDir;
        _voice = cfg.AzureVoice;
        _region = cfg.AzureSpeechRegion;
        _key = cfg.AzureSpeechKey;
        Enabled = cfg.TtsEnabled;

        Directory.CreateDirectory(_cacheDir);

        if (Enabled)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _key);
            _http.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "ogg-48khz-16bit-mono-opus");
            _http.DefaultRequestHeaders.Add("User-Agent", "EnglishBot");
        }
    }

    /// <summary>Returns OGG/Opus bytes, or null if audio is off or synthesis failed.</summary>
    public async Task<byte[]?> SpeakAsync(string text, CancellationToken ct)
    {
        if (!Enabled || _http is null || string.IsNullOrWhiteSpace(text)) return null;

        var path = CachePath(text);
        if (File.Exists(path))
        {
            try { return await File.ReadAllBytesAsync(path, ct); }
            catch (IOException) { /* fall through and re-synthesize */ }
        }

        try
        {
            var url = $"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(Ssml(text), Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[tts] {(int)res.StatusCode} {res.ReasonPhrase} — audio oʻchirildi shu soʻz uchun.");
                return null;
            }

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;

            await File.WriteAllBytesAsync(path, bytes, ct);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"[tts] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Slightly slowed speech — a beginner needs to hear the syllables, and the
    /// default rate is too fast to imitate.
    /// </summary>
    private string Ssml(string text)
    {
        var safe = System.Net.WebUtility.HtmlEncode(text);
        return $"""
            <speak version='1.0' xml:lang='en-US'>
              <voice xml:lang='en-US' name='{_voice}'>
                <prosody rate='-15%'>{safe}</prosody>
              </voice>
            </speak>
            """;
    }

    private string CachePath(string text)
    {
        var raw = $"{_voice}|{text}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        return Path.Combine(_cacheDir, $"{hash}.ogg");
    }

    public void Dispose() => _http?.Dispose();
}
