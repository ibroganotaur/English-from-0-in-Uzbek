using EnglishBot;
using EnglishBot.Bot;
using EnglishBot.Content;
using EnglishBot.Jobs;
using EnglishBot.Speech;
using EnglishBot.State;
using EnglishBot.Telegram;
using EnglishBot.Tools;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var root = AppContext.BaseDirectory;
var cfg = BotConfig.Load(root);

// `dotnet run -- --check` validates all content and screens offline. No token needed.
if (args.Contains("--check") || args.Contains("--selftest"))
    return SelfTest.Run(cfg);

if (!cfg.TokenLooksReal)
{
    Console.Error.WriteLine("""

        ┌─────────────────────────────────────────────────────────────┐
          Telegram tokeni topilmadi.

          1. Telegramda @BotFather ga yozing
          2. /newbot  ->  nom va username bering
          3. Bergan tokenini quyidagi joyga qoying:

             appsettings.Local.json
             { "token": "1234567:AA..." }

          yoki muhit oʻzgaruvchisi orqali:
             setx ENGLISHBOT_TOKEN "1234567:AA..."
        └─────────────────────────────────────────────────────────────┘

        """);
    return 1;
}

LessonStore lessons;
try
{
    lessons = LessonStore.Load(cfg.LessonsDir);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Darslarni yuklab boʻlmadi: {ex.Message}");
    return 1;
}

var store = new Store(cfg.DataDir);
using var tts = new Tts(cfg);
using var tg = new TelegramClient(cfg.Token);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

string me;
try
{
    me = await tg.GetMeAsync(cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Telegramga ulanib boʻlmadi: {ex.Message}");
    return 1;
}

await tg.SetMyCommandsAsync(new[]
{
    ("start",      "Boshlash"),
    ("dars",       "Bugungi dars"),
    ("takror",     "Soʻzlarni takrorlash"),
    ("bog",        "Mening bogʻim"),
    ("sozlamalar", "Eslatma vaqti")
}, cts.Token);

Console.WriteLine($"""
    🌷 Noldan Ingliz Tili
       bot        @{me}
       darslar    {lessons.Count} ta
       audio      {(tts.Enabled ? "yoqilgan" : "oʻchirilgan (Azure kaliti yoʻq)")}
       eslatma    har kuni {cfg.RemindHour:00}:00 (UTC+{cfg.TzOffsetMinutes / 60})
       maʼlumot   {cfg.DataDir}

    Toʻxtatish uchun Ctrl+C.
    """);

var router = new Router(tg, lessons, store, tts, cfg);
var nudge = new DailyNudge(tg, store, lessons, cfg).RunAsync(cts.Token);
var poll = PollAsync(tg, router, cts.Token);

await Task.WhenAll(nudge, poll);
Console.WriteLine("Toʻxtatildi.");
return 0;

static async Task PollAsync(TelegramClient tg, Router router, CancellationToken ct)
{
    long offset = 0;
    var backoff = TimeSpan.FromSeconds(2);

    while (!ct.IsCancellationRequested)
    {
        try
        {
            var updates = await tg.GetUpdatesAsync(offset, ct);
            backoff = TimeSpan.FromSeconds(2);

            foreach (var u in updates)
            {
                offset = u.UpdateId + 1;
                try
                {
                    await router.HandleAsync(u, ct);
                }
                catch (Exception ex)
                {
                    // One bad update must never take the bot down.
                    Console.Error.WriteLine($"[update {u.UpdateId}] {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[poll] {ex.Message}");
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }
}
