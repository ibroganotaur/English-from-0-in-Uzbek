using EnglishBot.Content;
using EnglishBot.Speech;
using EnglishBot.Srs;
using EnglishBot.State;
using EnglishBot.Telegram;
using EnglishBot.Ui;

namespace EnglishBot.Bot;

/// <summary>
/// Turns Telegram updates into screens. State lives in <see cref="Store"/> and in the
/// callback data itself, so the bot can be restarted mid-lesson without losing her place.
/// </summary>
public sealed class Router
{
    private readonly TelegramClient _tg;
    private readonly LessonStore _lessons;
    private readonly Store _store;
    private readonly Tts _tts;
    private readonly BotConfig _cfg;
    private readonly Random _rng = new();

    public Router(TelegramClient tg, LessonStore lessons, Store store, Tts tts, BotConfig cfg)
    {
        _tg = tg;
        _lessons = lessons;
        _store = store;
        _tts = tts;
        _cfg = cfg;
    }

    public async Task HandleAsync(TgUpdate update, CancellationToken ct)
    {
        if (update.Message is { } m && m.Text is { Length: > 0 })
            await OnTextAsync(m, ct);
        else if (update.CallbackQuery is { } cq)
            await OnCallbackAsync(cq, ct);
    }

    // ------------------------------------------------------------------ text

    private async Task OnTextAsync(TgMessage m, CancellationToken ct)
    {
        var userId = m.From?.Id ?? m.Chat.Id;
        var chatId = m.Chat.Id;

        if (m.Text!.Trim().StartsWith("/whoami", StringComparison.OrdinalIgnoreCase))
        {
            await _tg.SendAsync(chatId, $"Sizning Telegram id: <code>{userId}</code>", null, ct);
            return;
        }

        if (!_cfg.IsAllowed(userId))
        {
            await _tg.SendAsync(chatId, "Bu bot shaxsiy foydalanish uchun. 🌸", null, ct);
            return;
        }

        var user = _store.GetOrCreate(chatId, m.From?.FirstName ?? m.Chat.FirstName ?? "");
        var cmd = m.Text.Trim().Split(' ')[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/start":
                await _tg.SendAsync(chatId, Screens.Welcome(user.Name), Kb.Welcome(), ct);
                break;

            case "/dars":
            case "/lesson":
                await StartLessonAsync(chatId, null, user.CurrentLesson, 0, ct);
                break;

            case "/takror":
            case "/review":
                await StartReviewAsync(chatId, null, user, ct);
                break;

            case "/bog":
            case "/garden":
                await _tg.SendAsync(chatId, Screens.Garden(user, _lessons, _cfg.LocalNow()), Kb.Back(), ct);
                break;

            case "/sozlamalar":
            case "/settings":
                await _tg.SendAsync(chatId, Screens.Settings(user), Kb.Settings(user), ct);
                break;

            default:
                await ShowHomeAsync(chatId, null, user, ct);
                break;
        }

        await _store.SaveAsync(ct);
    }

    // -------------------------------------------------------------- callback

    /// <summary>
    /// Stops the spinner in her Telegram. Purely cosmetic, and allowed to fail:
    /// Telegram rejects a callback id more than a few seconds old, so every tap made
    /// while the bot was down would otherwise throw here and take the tap with it.
    /// Acknowledging is never worth losing the work the tap asked for.
    /// </summary>
    private async Task AckAsync(string callbackId, string? toast, CancellationToken ct)
    {
        try { await _tg.AnswerCallbackAsync(callbackId, toast, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Console.Error.WriteLine($"[ack] {ex.Message}"); }
    }


    private async Task OnCallbackAsync(TgCallbackQuery cq, CancellationToken ct)
    {
        var chatId = cq.Message?.Chat.Id ?? cq.From.Id;
        var msgId = cq.Message?.MessageId ?? 0;

        if (!_cfg.IsAllowed(cq.From.Id))
        {
            await AckAsync(cq.Id, "Bu bot shaxsiy foydalanish uchun.", ct);
            return;
        }

        var user = _store.GetOrCreate(chatId, cq.From.FirstName ?? "");
        var parts = (cq.Data ?? "").Split(':');
        var verb = parts.Length > 0 ? parts[0] : "";

        // Acknowledge immediately so her Telegram stops showing the spinner.
        await AckAsync(cq.Id, null, ct);

        switch (verb)
        {
            case "home":
                await ShowHomeAsync(chatId, msgId, user, ct);
                break;

            case "lesson" when parts.Length == 3:
                await StartLessonAsync(chatId, msgId, int.Parse(parts[1]), int.Parse(parts[2]), ct);
                break;

            case "quiz" when parts.Length == 3:
                await ShowQuestionAsync(chatId, msgId, user, int.Parse(parts[1]), int.Parse(parts[2]), ct);
                break;

            case "ans" when parts.Length == 4:
                await AnswerAsync(chatId, msgId, user, int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]), ct);
                break;

            case "done" when parts.Length == 2:
                await FinishLessonAsync(chatId, msgId, user, int.Parse(parts[1]), ct);
                break;

            case "say" when parts.Length == 3:
                await SpeakAsync(chatId, cq.Id, int.Parse(parts[1]), int.Parse(parts[2]), ct);
                break;

            case "garden":
                await _tg.EditAsync(chatId, msgId, Screens.Garden(user, _lessons, _cfg.LocalNow()), Kb.Back(), ct);
                break;

            case "list":
                await _tg.EditAsync(chatId, msgId, "📚 <b>Darslar</b>\n\n<i>Istalgan darsni qayta koʻrishingiz mumkin.</i>",
                                    Kb.LessonList(_lessons, user), ct);
                break;

            case "settings":
                await _tg.EditAsync(chatId, msgId, Screens.Settings(user), Kb.Settings(user), ct);
                break;

            case "hour" when parts.Length == 2:
                user.RemindHour = int.Parse(parts[1]);
                user.RemindersOn = true;
                await _tg.EditAsync(chatId, msgId, Screens.Settings(user), Kb.Settings(user), ct);
                break;

            case "remind" when parts.Length == 2:
                user.RemindersOn = parts[1] == "1";
                await _tg.EditAsync(chatId, msgId, Screens.Settings(user), Kb.Settings(user), ct);
                break;

            case "review":
                await StartReviewAsync(chatId, msgId, user, ct);
                break;

            case "rans" when parts.Length == 2:
                await ReviewAnswerAsync(chatId, msgId, user, int.Parse(parts[1]), ct);
                break;

            case "rnext":
                await ReviewNextAsync(chatId, msgId, user, ct);
                break;
        }

        await _store.SaveAsync(ct);
    }

    // ------------------------------------------------------------------ home

    private async Task ShowHomeAsync(long chatId, long? msgId, UserState user, CancellationToken ct)
    {
        var due = Leitner.DueCount(user, _cfg.LocalNow());
        var html = Screens.Home(user, _lessons, due, _cfg.LocalNow());
        var kb = Kb.Home(user, _lessons, due);

        if (msgId is { } id) await _tg.EditAsync(chatId, id, html, kb, ct);
        else await _tg.SendAsync(chatId, html, kb, ct);
    }

    // ---------------------------------------------------------------- lesson

    private async Task StartLessonAsync(long chatId, long? msgId, int lessonId, int step, CancellationToken ct)
    {
        var lesson = _lessons.Get(lessonId);
        if (lesson is null)
        {
            var html = Screens.AllDone();
            if (msgId is { } id0) await _tg.EditAsync(chatId, id0, html, Kb.Back(), ct);
            else await _tg.SendAsync(chatId, html, Kb.Back(), ct);
            return;
        }

        step = Math.Clamp(step, 0, Screens.StepCount(lesson) - 1);

        var body = Screens.Step(lesson, step);
        var kb = step == 0
            ? Kb.LessonWords(lesson, _tts.Enabled)
            : Kb.LessonStep(lesson, step);

        if (msgId is { } id) await _tg.EditAsync(chatId, id, body, kb, ct);
        else await _tg.SendAsync(chatId, body, kb, ct);
    }

    private async Task ShowQuestionAsync(long chatId, long msgId, UserState user, int lessonId, int qi, CancellationToken ct)
    {
        var lesson = _lessons.Get(lessonId);
        if (lesson is null || lesson.Quiz.Count == 0)
        {
            await FinishLessonAsync(chatId, msgId, user, lessonId, ct);
            return;
        }

        if (qi <= 0 || user.Quiz is null || user.Quiz.LessonId != lessonId)
            user.Quiz = new QuizRun { LessonId = lessonId };

        qi = Math.Clamp(qi, 0, lesson.Quiz.Count - 1);

        await _tg.EditAsync(chatId, msgId, Screens.QuizQuestion(lesson, qi), Kb.QuizOptions(lesson, qi), ct);
    }

    private async Task AnswerAsync(long chatId, long msgId, UserState user, int lessonId, int qi, int oi, CancellationToken ct)
    {
        var lesson = _lessons.Get(lessonId);
        if (lesson is null || qi >= lesson.Quiz.Count) return;

        var q = lesson.Quiz[qi];
        if (oi < 0 || oi >= q.Options.Count) return;

        user.Quiz ??= new QuizRun { LessonId = lessonId };
        var correct = q.Options[oi].Correct;

        // Guard against a double-tap on an already-answered question.
        if (user.Quiz.Answered <= qi)
        {
            user.Quiz.Answered = qi + 1;
            if (correct) user.Quiz.Correct++;
        }

        // A missed question drags the words it actually exercises back into the review deck —
        // the ones in its correct answer, not the first four words of the lesson.
        if (!correct)
        {
            var now = _cfg.LocalNow();
            foreach (var wid in _lessons.WordIdsInQuestion(lessonId, qi))
                Leitner.Record(user, wid, false, now);
        }

        await _tg.EditAsync(chatId, msgId, Screens.QuizFeedback(lesson, qi, oi), Kb.QuizNext(lesson, qi), ct);
    }

    private async Task FinishLessonAsync(long chatId, long msgId, UserState user, int lessonId, CancellationToken ct)
    {
        var lesson = _lessons.Get(lessonId);
        if (lesson is null) return;

        var now = _cfg.LocalNow();
        var today = Leitner.Today(now);

        var score = user.Quiz?.LessonId == lessonId ? user.Quiz.Correct : 0;
        var total = lesson.Quiz.Count;
        var firstTime = !user.Lessons.ContainsKey(lessonId);

        // Keep the best score she has ever got on this lesson.
        if (!user.Lessons.TryGetValue(lessonId, out var prev) || score >= prev.Score)
            user.Lessons[lessonId] = new LessonResult { Score = score, Total = total, Date = today };

        // Every word of a finished lesson enters the spaced-repetition deck.
        Leitner.Seed(user, _lessons.WordIdsFor(lessonId), now);

        BumpStreak(user, now);

        if (lessonId == user.CurrentLesson && lessonId < _lessons.All[^1].Id)
            user.CurrentLesson = lessonId + 1;
        else if (lessonId == user.CurrentLesson)
            user.CurrentLesson = lessonId;   // last lesson: stay put

        user.Quiz = null;

        var due = Leitner.DueCount(user, now);
        await _tg.EditAsync(chatId, msgId,
            Screens.LessonDone(lesson, user, score, total, firstTime),
            Kb.AfterLesson(_lessons, user, due), ct);
    }

    /// <summary>
    /// Streak advances once per local day. Missing days resets it to 1 rather than 0 —
    /// coming back today is still a day studied, and nothing in her garden is destroyed.
    /// </summary>
    private static void BumpStreak(UserState user, DateTime localNow)
    {
        var today = Leitner.Today(localNow);
        if (user.LastStudyDate == today) return;

        var yesterday = localNow.AddDays(-1).ToString("yyyy-MM-dd");
        user.Streak = user.LastStudyDate == yesterday ? user.Streak + 1 : 1;
        user.BestStreak = Math.Max(user.BestStreak, user.Streak);
        user.LastStudyDate = today;
    }

    // ----------------------------------------------------------------- audio

    private async Task SpeakAsync(long chatId, string callbackId, int lessonId, int wi, CancellationToken ct)
    {
        var lesson = _lessons.Get(lessonId);
        if (lesson is null || wi < 0 || wi >= lesson.Words.Count) return;

        var word = lesson.Words[wi];
        var ogg = await _tts.SpeakAsync(word.Speakable, ct);

        if (ogg is null)
        {
            await AckAsync(callbackId, "Audio hozir ishlamayapti.", ct);
            return;
        }

        await _tg.SendVoiceAsync(chatId, ogg,
            $"<b>{Deco.Esc(word.En)}</b>  ·  <code>{Deco.Esc(word.Pron)}</code>\n<i>{Deco.Esc(word.Uz)}</i>", ct);
    }

    // ---------------------------------------------------------------- review

    private async Task StartReviewAsync(long chatId, long? msgId, UserState user, CancellationToken ct)
    {
        var now = _cfg.LocalNow();
        var queue = Leitner.Due(user, now, 10);

        if (queue.Count == 0)
        {
            const string html = "🌿 <b>Hammasi takrorlangan</b>\n\nBugun takrorlash kerak boʻlgan soʻz yoʻq. Yangi dars oʻting — yangi soʻzlar qoʻshiladi.";
            if (msgId is { } id) await _tg.EditAsync(chatId, id, html, Kb.Back(), ct);
            else await _tg.SendAsync(chatId, html, Kb.Back(), ct);
            return;
        }

        user.Review = new ReviewRun { Queue = queue, Index = 0, Correct = 0 };
        await ShowReviewQuestionAsync(chatId, msgId, user, ct);
    }

    private async Task ShowReviewQuestionAsync(long chatId, long? msgId, UserState user, CancellationToken ct)
    {
        var run = user.Review;
        if (run is null || run.Index >= run.Queue.Count)
        {
            await FinishReviewAsync(chatId, msgId, user, ct);
            return;
        }

        var word = _lessons.GetWord(run.Queue[run.Index]);
        if (word is null)
        {
            run.Index++;
            await ShowReviewQuestionAsync(chatId, msgId, user, ct);
            return;
        }

        run.Options = BuildOptions(run.Queue[run.Index]);
        var labels = run.Options.Select(id => _lessons.GetWord(id)?.Uz ?? "?").ToList();

        var html = Screens.ReviewQuestion(word, run.Index, run.Queue.Count);
        var kb = Kb.ReviewOptions(labels);

        if (msgId is { } id) await _tg.EditAsync(chatId, id, html, kb, ct);
        else await _tg.SendAsync(chatId, html, kb, ct);
    }

    /// <summary>Correct word plus three decoys, shuffled.</summary>
    private List<string> BuildOptions(string correctId)
    {
        var correctWord = _lessons.GetWord(correctId);

        var pool = _lessons.All
            .SelectMany(l => Enumerable.Range(0, l.Words.Count).Select(i => l.WordId(i)))
            .Where(id => id != correctId)
            .Where(id => _lessons.GetWord(id)?.Uz != correctWord?.Uz)   // no duplicate labels
            .OrderBy(_ => _rng.Next())
            .Take(3)
            .ToList();

        pool.Add(correctId);
        return pool.OrderBy(_ => _rng.Next()).ToList();
    }

    private async Task ReviewAnswerAsync(long chatId, long msgId, UserState user, int oi, CancellationToken ct)
    {
        var run = user.Review;
        if (run is null || run.Index >= run.Queue.Count) return;
        if (oi < 0 || oi >= run.Options.Count) return;

        var correctId = run.Queue[run.Index];
        var chosenId = run.Options[oi];
        var correct = chosenId == correctId;

        var word = _lessons.GetWord(correctId);
        if (word is null) return;

        if (correct) run.Correct++;
        Leitner.Record(user, correctId, correct, _cfg.LocalNow());

        var chosenLabel = _lessons.GetWord(chosenId)?.Uz ?? "?";
        await _tg.EditAsync(chatId, msgId, Screens.ReviewFeedback(word, correct, chosenLabel), Kb.ReviewNext(), ct);
    }

    private async Task ReviewNextAsync(long chatId, long msgId, UserState user, CancellationToken ct)
    {
        if (user.Review is null) { await ShowHomeAsync(chatId, msgId, user, ct); return; }

        user.Review.Index++;
        await ShowReviewQuestionAsync(chatId, msgId, user, ct);
    }

    private async Task FinishReviewAsync(long chatId, long? msgId, UserState user, CancellationToken ct)
    {
        var run = user.Review;
        var correct = run?.Correct ?? 0;
        var total = run?.Queue.Count ?? 0;

        user.Review = null;
        BumpStreak(user, _cfg.LocalNow());

        var html = Screens.ReviewDone(correct, total);
        if (msgId is { } id) await _tg.EditAsync(chatId, id, html, Kb.Back(), ct);
        else await _tg.SendAsync(chatId, html, Kb.Back(), ct);
    }
}
