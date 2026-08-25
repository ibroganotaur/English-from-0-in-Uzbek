using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishBot.Telegram;

// ---------------------------------------------------------------------------
// Minimal Bot API types. Only the fields this bot actually reads.
// ---------------------------------------------------------------------------

public sealed class TgUser
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? Username { get; set; }
}

public sealed class TgChat
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
}

public sealed class TgMessage
{
    public long MessageId { get; set; }
    public TgChat Chat { get; set; } = new();
    public TgUser? From { get; set; }
    public string? Text { get; set; }
}

public sealed class TgCallbackQuery
{
    public string Id { get; set; } = "";
    public TgUser From { get; set; } = new();
    public TgMessage? Message { get; set; }
    public string? Data { get; set; }
}

public sealed class TgUpdate
{
    public long UpdateId { get; set; }
    public TgMessage? Message { get; set; }
    public TgCallbackQuery? CallbackQuery { get; set; }
}

public sealed class TgResponse<T>
{
    public bool Ok { get; set; }
    public T? Result { get; set; }
    public string? Description { get; set; }
    public int? ErrorCode { get; set; }
}

/// <summary>An inline keyboard button. Only callback buttons are used here.</summary>
public sealed record Btn(string Text, string Data)
{
    public static Btn Of(string text, string data) => new(text, data);
}

/// <summary>
/// Thin Telegram Bot API client over HttpClient. Deliberately dependency-free:
/// the Bot API is stable and this is ~6 methods, so there is no library to
/// keep up with.
/// </summary>
public sealed class TelegramClient : IDisposable
{
    private readonly HttpClient _http;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TelegramClient(string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"https://api.telegram.org/bot{token}/"),
            // Long polling holds the connection open; must exceed the poll timeout.
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    private async Task<T?> CallAsync<T>(string method, object? payload, CancellationToken ct)
    {
        using var res = payload is null
            ? await _http.GetAsync(method, ct)
            : await _http.PostAsJsonAsync(method, payload, Json, ct);

        var body = await res.Content.ReadFromJsonAsync<TgResponse<T>>(Json, ct);

        if (body is null)
            throw new InvalidOperationException($"{method}: javob boʻsh keldi.");

        if (!body.Ok)
            throw new InvalidOperationException($"{method}: {body.ErrorCode} {body.Description}");

        return body.Result;
    }

    public async Task<string> GetMeAsync(CancellationToken ct)
    {
        var me = await CallAsync<TgUser>("getMe", null, ct);
        return me?.Username ?? "?";
    }

    public async Task<IReadOnlyList<TgUpdate>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        var payload = new
        {
            offset,
            timeout = 50,
            allowed_updates = new[] { "message", "callback_query" }
        };
        return await CallAsync<List<TgUpdate>>("getUpdates", payload, ct) ?? new List<TgUpdate>();
    }

    public Task SetMyCommandsAsync(IEnumerable<(string Command, string Description)> commands, CancellationToken ct)
        => CallAsync<object>("setMyCommands", new
        {
            commands = commands.Select(c => new { command = c.Command, description = c.Description }).ToArray()
        }, ct);

    public async Task<long> SendAsync(long chatId, string html, IEnumerable<IEnumerable<Btn>>? keyboard = null,
                                      CancellationToken ct = default)
    {
        var payload = new
        {
            chat_id = chatId,
            text = html,
            parse_mode = "HTML",
            link_preview_options = new { is_disabled = true },
            reply_markup = Markup(keyboard)
        };
        var msg = await CallAsync<TgMessage>("sendMessage", payload, ct);
        return msg?.MessageId ?? 0;
    }

    /// <summary>
    /// Edits a message in place. Telegram rejects a no-op edit with 400
    /// "message is not modified" — harmless, so it is swallowed.
    /// </summary>
    public async Task EditAsync(long chatId, long messageId, string html,
                                IEnumerable<IEnumerable<Btn>>? keyboard = null, CancellationToken ct = default)
    {
        var payload = new
        {
            chat_id = chatId,
            message_id = messageId,
            text = html,
            parse_mode = "HTML",
            link_preview_options = new { is_disabled = true },
            reply_markup = Markup(keyboard)
        };
        try
        {
            await CallAsync<object>("editMessageText", payload, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not modified"))
        {
            // Same content re-rendered. Nothing to do.
        }
    }

    public Task AnswerCallbackAsync(string callbackId, string? toast = null, CancellationToken ct = default)
        => CallAsync<object>("answerCallbackQuery", new
        {
            callback_query_id = callbackId,
            text = toast,
            show_alert = false
        }, ct);

    /// <summary>Sends an OGG/Opus blob as a Telegram voice note.</summary>
    public async Task SendVoiceAsync(long chatId, byte[] ogg, string caption, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString()), "chat_id");
        form.Add(new StringContent(caption), "caption");
        form.Add(new StringContent("HTML"), "parse_mode");

        var file = new ByteArrayContent(ogg);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        form.Add(file, "voice", "word.ogg");

        using var res = await _http.PostAsync("sendVoice", form, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"sendVoice: {(int)res.StatusCode} {body}");
        }
    }

    private static object? Markup(IEnumerable<IEnumerable<Btn>>? keyboard)
    {
        if (keyboard is null) return null;

        var rows = keyboard
            .Select(row => row.Select(b => new { text = b.Text, callback_data = b.Data }).ToArray())
            .Where(row => row.Length > 0)
            .ToArray();

        return rows.Length == 0 ? null : new { inline_keyboard = rows };
    }

    public void Dispose() => _http.Dispose();
}
