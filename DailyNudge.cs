using EnglishBot.Content;
using EnglishBot.Srs;
using EnglishBot.State;
using EnglishBot.Telegram;
using EnglishBot.Ui;

namespace EnglishBot.Jobs;

/// <summary>
/// The single most valuable thing the bot does: show up at the same time every
/// evening. Self-study dies from forgetting, not from difficulty.
/// </summary>
public sealed class DailyNudge
{
    private readonly TelegramClient _tg;
    private readonly Store _store;
    private readonly LessonStore _lessons;
    private readonly BotConfig _cfg;

    public DailyNudge(TelegramClient tg, Store store, LessonStore lessons, BotConfig cfg)
    {
        _tg = tg;
        _store = store;
        _lessons = lessons;
        _cfg = cfg;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Let the poll loop come up first.
        try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[nudge] {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = _cfg.LocalNow();
        var today = Leitner.Today(now);
        var dirty = false;

        foreach (var user in _store.Everyone())
        {
            if (!user.RemindersOn) continue;
            if (user.LastNudgeDate == today) continue;      // already nudged today
            if (user.LastStudyDate == today) continue;      // she already studied, leave her alone
            if (now.Hour < user.RemindHour) continue;       // not time yet

            var next = _lessons.Get(user.CurrentLesson);

            try
            {
                await _tg.SendAsync(user.ChatId, Screens.Nudge(user, next), Kb.Nudge(next), ct);
                user.LastNudgeDate = today;
                dirty = true;
            }
            catch (Exception ex)
            {
                // Blocked bot, deleted chat — record it so we don't retry all evening.
                Console.Error.WriteLine($"[nudge] {user.ChatId}: {ex.Message}");
                user.LastNudgeDate = today;
                dirty = true;
            }
        }

        if (dirty) await _store.SaveAsync(ct);
    }
}
