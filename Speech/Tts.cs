using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace EnglishBot.Speech;

/// <summary>
/// Turns an English word or sentence into a Telegram voice note.
///
/// Two backends, picked automatically:
///
///   Windows  (default, free)  Windows' own speech engine writes a WAV, then VLC — which
///                             is already on most machines — transcodes it to MP3.
///                             Telegram accepts MP3 for voice notes. No account, no key,
///                             no per-word cost, works offline.
///
///   Azure    (optional)       Better-sounding neural voices, returns OGG/Opus directly.
///                             Used only when an Azure key is configured.
///
/// With neither available, <see cref="Enabled"/> is false and the bot runs without audio.
/// Every clip is cached on disk, so each word costs one synthesis ever.
/// </summary>
public sealed class Tts : IDisposable
{
    private enum Backend { None, Azure, Windows }

    private static readonly string[] VlcCandidates =
    {
        @"C:\Program Files\VideoLAN\VLC\vlc.exe",
        @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
    };

    private readonly Backend _backend;
    private readonly HttpClient? _http;
    private readonly string _cacheDir;
    private readonly string _voice;        // Azure voice name
    private readonly string _region;
    private readonly string _key;
    private readonly string _winVoice;     // Windows voice name
    private readonly int _winRate;         // -10..10, negative is slower
    private readonly string _vlc;

    public bool Enabled => _backend != Backend.None;

    /// <summary>What the chosen backend produces — Telegram needs both of these to match.</summary>
    public string Mime => _backend == Backend.Azure ? "audio/ogg" : "audio/mpeg";
    public string FileName => _backend == Backend.Azure ? "word.ogg" : "word.mp3";

    /// <summary>Shown in the startup banner.</summary>
    public string Describe => _backend switch
    {
        Backend.Azure => "yoqilgan (Azure)",
        Backend.Windows => $"yoqilgan (Windows: {_winVoice})",
        _ => "oʻchirilgan (ovoz manbai topilmadi)"
    };

    public Tts(BotConfig cfg)
    {
        _cacheDir = cfg.AudioDir;
        _voice = cfg.AzureVoice;
        _region = cfg.AzureSpeechRegion;
        _key = cfg.AzureSpeechKey;
        _winVoice = cfg.WindowsVoice;
        _winRate = cfg.SpeechRate;
        _vlc = ResolveVlc(cfg.VlcPath);

        Directory.CreateDirectory(_cacheDir);

        if (!string.IsNullOrWhiteSpace(_key))
        {
            _backend = Backend.Azure;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _key);
            _http.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "ogg-48khz-16bit-mono-opus");
            _http.DefaultRequestHeaders.Add("User-Agent", "EnglishBot");
        }
        else if (OperatingSystem.IsWindows() && _vlc.Length > 0)
        {
            _backend = Backend.Windows;
        }
        else
        {
            _backend = Backend.None;
        }
    }

    private static string ResolveVlc(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : "";

        foreach (var p in VlcCandidates)
            if (File.Exists(p)) return p;

        return "";
    }

    /// <summary>Audio bytes in <see cref="Mime"/> format, or null if audio is off or synthesis failed.</summary>
    public async Task<byte[]?> SpeakAsync(string text, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return null;

        var path = CachePath(text);
        if (File.Exists(path))
        {
            try { return await File.ReadAllBytesAsync(path, ct); }
            catch (IOException) { /* fall through and re-synthesize */ }
        }

        var bytes = _backend == Backend.Azure
            ? await AzureAsync(text, ct)
            : await WindowsAsync(text, ct);

        if (bytes is null || bytes.Length == 0) return null;

        try { await File.WriteAllBytesAsync(path, bytes, ct); }
        catch (IOException) { /* cache is a nicety, not a requirement */ }

        return bytes;
    }

    // ------------------------------------------------------------------ Windows

    /// <summary>
    /// Windows speech writes WAV only, and Telegram will not take WAV for a voice note,
    /// so VLC transcodes it to MP3. Both tools already exist on the machine; nothing is
    /// downloaded and nothing is billed.
    /// </summary>
    private async Task<byte[]?> WindowsAsync(string text, CancellationToken ct)
    {
        var stem = Path.Combine(Path.GetTempPath(), $"englishbot-{Guid.NewGuid():N}");
        var wav = stem + ".wav";
        var mp3 = stem + ".mp3";
        var ps1 = stem + ".ps1";

        try
        {
            await File.WriteAllTextAsync(ps1, SynthScript(text, wav), new UTF8Encoding(true), ct);

            if (!await RunAsync("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ps1}\"",
                    TimeSpan.FromSeconds(30), ct) || !File.Exists(wav))
            {
                Console.Error.WriteLine("[tts] Windows ovozi WAV yarata olmadi.");
                return null;
            }

            var sout = $":sout=#transcode{{acodec=mp3,ab=64,channels=1,samplerate=24000}}"
                     + $":standard{{access=file,mux=raw,dst={mp3}}}";

            await RunAsync(_vlc,
                $"-I dummy --no-video --quiet \"{wav}\" \"{sout}\" vlc://quit",
                TimeSpan.FromSeconds(30), ct);

            // VLC's exit code is unreliable; the file is the real answer.
            if (!File.Exists(mp3)) { Console.Error.WriteLine("[tts] VLC MP3 yarata olmadi."); return null; }

            return await File.ReadAllBytesAsync(mp3, ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[tts] {ex.Message}");
            return null;
        }
        finally
        {
            foreach (var f in new[] { wav, mp3, ps1 })
                try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { }
        }
    }

    // $$ raw string: {{...}} interpolates, a single { stays literal — which PowerShell needs.
    private string SynthScript(string text, string wavPath) => $$"""
        Add-Type -AssemblyName System.Speech
        $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
        try { $s.SelectVoice('{{Ps(_winVoice)}}') } catch { }
        $s.Rate = {{_winRate}}
        $s.SetOutputToWaveFile('{{Ps(wavPath)}}')
        $s.Speak('{{Ps(text)}}')
        $s.Dispose()
        """;

    /// <summary>PowerShell single-quoted strings escape a quote by doubling it.</summary>
    private static string Ps(string s) => s.Replace("'", "''");

    private static async Task<bool> RunAsync(string exe, string args, TimeSpan timeout, CancellationToken ct)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        if (!p.Start()) return false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return false;
        }
    }

    // -------------------------------------------------------------------- Azure

    private async Task<byte[]?> AzureAsync(string text, CancellationToken ct)
    {
        if (_http is null) return null;

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

            return await res.Content.ReadAsByteArrayAsync(ct);
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
        // The backend is part of the key: the same word sounds different from each engine.
        var raw = $"{_backend}|{(_backend == Backend.Azure ? _voice : _winVoice)}|{_winRate}|{text}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
        var ext = _backend == Backend.Azure ? "ogg" : "mp3";
        return Path.Combine(_cacheDir, $"{hash}.{ext}");
    }

    public void Dispose() => _http?.Dispose();
}
