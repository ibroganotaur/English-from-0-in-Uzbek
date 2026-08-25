using System.Text.RegularExpressions;
using EnglishBot.Content;
using EnglishBot.Srs;
using EnglishBot.State;
using EnglishBot.Ui;

namespace EnglishBot.Tools;

/// <summary>
/// `dotnet run -- --check` — validates the lesson JSON and renders every screen the
/// bot can produce, without touching Telegram. Run it after editing any lesson file.
/// </summary>
public static class SelfTest
{
    // Tags Telegram accepts in parse_mode=HTML. Anything else is a 400 at runtime.
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "strong", "i", "em", "u", "ins", "s", "strike", "del",
        "a", "code", "pre", "tg-spoiler", "blockquote"
    };

    private const int TelegramMessageLimit = 4096;
    private const int CallbackDataLimit = 64;
    private const int ComfortableButtonLength = 45;

    public static int Run(BotConfig cfg)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        LessonStore lessons;
        try
        {
            lessons = LessonStore.Load(cfg.LessonsDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Darslarni yuklab boʻlmadi: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Tekshirilmoqda: {lessons.Count} ta dars\n");

        CheckLessons(lessons, errors, warnings);
        CheckRendering(lessons, errors, warnings);
        CheckScheduler(lessons, errors);

        Console.WriteLine();
        foreach (var w in warnings) Console.WriteLine($"⚠️  {w}");
        foreach (var e in errors) Console.WriteLine($"❌ {e}");

        if (errors.Count == 0)
        {
            var totalWords = lessons.All.Sum(l => l.Words.Count);
            var totalQuiz = lessons.All.Sum(l => l.Quiz.Count);
            var totalOptions = lessons.All.Sum(l => l.Quiz.Sum(q => q.Options.Count));

            Console.WriteLine($"""

                ✅ Hammasi joyida.
                   darslar        {lessons.Count}
                   soʻzlar        {totalWords}
                   savollar       {totalQuiz}
                   javob varianti {totalOptions}
                   ogohlantirish  {warnings.Count}
                """);
            return 0;
        }

        Console.WriteLine($"\n{errors.Count} ta xato topildi.");
        return 1;
    }

    // ------------------------------------------------------------- content

    private static void CheckLessons(LessonStore lessons, List<string> errors, List<string> warnings)
    {
        var seenIds = new HashSet<int>();

        foreach (var l in lessons.All)
        {
            var tag = $"{l.Id}-dars";

            if (!seenIds.Add(l.Id)) errors.Add($"{tag}: id takrorlangan.");
            if (string.IsNullOrWhiteSpace(l.Title)) errors.Add($"{tag}: sarlavha yoʻq.");
            if (string.IsNullOrWhiteSpace(l.Flower)) errors.Add($"{tag}: gul belgisi yoʻq.");
            if (l.Words.Count == 0) errors.Add($"{tag}: soʻzlar yoʻq.");
            if (l.Rules.Count == 0) errors.Add($"{tag}: qoida yoʻq.");
            if (l.Quiz.Count == 0) errors.Add($"{tag}: mashq yoʻq.");

            foreach (var w in l.Words)
            {
                if (string.IsNullOrWhiteSpace(w.En)) errors.Add($"{tag}: soʻzning inglizchasi boʻsh.");
                if (string.IsNullOrWhiteSpace(w.Uz)) errors.Add($"{tag}: «{w.En}» — oʻzbekchasi yoʻq.");
                if (string.IsNullOrWhiteSpace(w.Pron)) warnings.Add($"{tag}: «{w.En}» — talaffuzi yoʻq.");
            }

            // Duplicate Uzbek glosses inside one lesson make review options ambiguous.
            var dupes = l.Words.GroupBy(w => w.Uz).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var d in dupes) warnings.Add($"{tag}: «{d}» tarjimasi ikki marta ishlatilgan.");

            foreach (var r in l.Rules) CheckHtml(r.Body, $"{tag} qoida «{r.Label}»", errors);
            if (l.Warning is not null) CheckHtml(l.Warning.Body, $"{tag} diqqat", errors);

            for (var qi = 0; qi < l.Quiz.Count; qi++)
            {
                var q = l.Quiz[qi];
                var qtag = $"{tag} mashq {qi + 1}";

                if (string.IsNullOrWhiteSpace(q.Prompt)) errors.Add($"{qtag}: savol matni boʻsh.");

                var correct = q.Options.Count(o => o.Correct);
                if (correct != 1) errors.Add($"{qtag}: toʻgʻri javoblar soni {correct} (1 boʻlishi kerak).");
                if (q.Options.Count < 2) errors.Add($"{qtag}: kamida 2 ta variant kerak.");

                var texts = new HashSet<string>();
                foreach (var o in q.Options)
                {
                    if (string.IsNullOrWhiteSpace(o.Text)) errors.Add($"{qtag}: variant matni boʻsh.");
                    if (!texts.Add(o.Text)) errors.Add($"{qtag}: «{o.Text}» varianti takrorlangan.");

                    // Every distractor must teach something — that is the whole design.
                    if (!o.Correct && string.IsNullOrWhiteSpace(o.Why))
                        errors.Add($"{qtag}: «{o.Text}» uchun izoh (why) yoʻq.");

                    if (o.Why is not null) CheckHtml(o.Why, $"{qtag} izoh", errors);

                    if (o.Text.Length > ComfortableButtonLength)
                        warnings.Add($"{qtag}: «{o.Text}» tugmasi uzun ({o.Text.Length} belgi) — telefonda kesilishi mumkin.");

                    // Button labels must not contain markup: Telegram renders them literally.
                    if (o.Text.Contains('<'))
                        errors.Add($"{qtag}: «{o.Text}» ichida HTML teg bor — tugmada teg ishlamaydi.");
                }
            }
        }
    }

    /// <summary>Rejects tags Telegram does not accept, and unbalanced ones.</summary>
    private static void CheckHtml(string html, string where, List<string> errors)
    {
        var stack = new Stack<string>();

        foreach (Match m in Regex.Matches(html, "</?([a-zA-Z-]+)[^>]*>"))
        {
            var name = m.Groups[1].Value;
            var closing = m.Value.StartsWith("</");

            if (!AllowedTags.Contains(name))
            {
                errors.Add($"{where}: <{name}> tegi Telegramda ishlamaydi.");
                continue;
            }

            if (closing)
            {
                if (stack.Count == 0 || !string.Equals(stack.Pop(), name, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{where}: </{name}> yopilishi mos kelmayapti.");
            }
            else
            {
                stack.Push(name);
            }
        }

        foreach (var open in stack)
            errors.Add($"{where}: <{open}> yopilmagan.");

        // A bare & that is not an entity breaks Telegram's HTML parser.
        foreach (Match m in Regex.Matches(html, "&(?!(amp|lt|gt|quot|#\\d+);)"))
            errors.Add($"{where}: '&' belgisi &amp; deb yozilishi kerak (pozitsiya {m.Index}).");
    }

    // ----------------------------------------------------------- rendering

    private static void CheckRendering(LessonStore lessons, List<string> errors, List<string> warnings)
    {
        var user = new UserState { ChatId = 1, Name = "Aziza", Streak = 5, BestStreak = 9 };
        var now = new DateTime(2026, 8, 21, 20, 0, 0);

        void Check(string html, string where)
        {
            if (string.IsNullOrWhiteSpace(html)) { errors.Add($"{where}: boʻsh ekran."); return; }
            if (html.Length > TelegramMessageLimit)
                errors.Add($"{where}: {html.Length} belgi — Telegram chegarasi {TelegramMessageLimit}.");
            else if (html.Length > TelegramMessageLimit * 0.85)
                warnings.Add($"{where}: {html.Length} belgi — chegaraga yaqin.");
            CheckHtml(html, where, errors);
        }

        void CheckKeyboard(IEnumerable<IEnumerable<Telegram.Btn>> kb, string where)
        {
            foreach (var b in kb.SelectMany(r => r))
            {
                var bytes = System.Text.Encoding.UTF8.GetByteCount(b.Data);
                if (bytes > CallbackDataLimit)
                    errors.Add($"{where}: callback_data «{b.Data}» {bytes} bayt — chegara {CallbackDataLimit}.");
                if (string.IsNullOrWhiteSpace(b.Text))
                    errors.Add($"{where}: tugma matni boʻsh.");
            }
        }

        Check(Screens.Welcome("Aziza"), "Welcome");
        Check(Screens.Home(user, lessons, 5, now), "Home");
        Check(Screens.Garden(user, lessons, now), "Garden");
        Check(Screens.Settings(user), "Settings");
        Check(Screens.ReviewDone(4, 5), "ReviewDone");
        Check(Screens.AllDone(), "AllDone");
        Check(Screens.Nudge(user, lessons.Get(1)), "Nudge");

        CheckKeyboard(Kb.Welcome(), "Kb.Welcome");
        CheckKeyboard(Kb.Home(user, lessons, 5), "Kb.Home");
        CheckKeyboard(Kb.Settings(user), "Kb.Settings");
        CheckKeyboard(Kb.LessonList(lessons, user), "Kb.LessonList");

        foreach (var l in lessons.All)
        {
            var steps = Screens.StepCount(l);
            for (var s = 0; s < steps; s++)
            {
                Check(Screens.Step(l, s), $"{l.Id}-dars qadam {s}");
                CheckKeyboard(s == 0 ? Kb.LessonWords(l, true) : Kb.LessonStep(l, s), $"{l.Id}-dars tugmalar {s}");
            }

            // The last teaching card must hand off to the quiz, or she gets stuck.
            if (!Kb.StepIsLast(l, steps - 1))
                errors.Add($"{l.Id}-dars: oxirgi qadamdan mashqqa oʻtish yoʻq.");

            for (var qi = 0; qi < l.Quiz.Count; qi++)
            {
                Check(Screens.QuizQuestion(l, qi), $"{l.Id}-dars savol {qi + 1}");
                CheckKeyboard(Kb.QuizOptions(l, qi), $"{l.Id}-dars variantlar {qi + 1}");

                for (var oi = 0; oi < l.Quiz[qi].Options.Count; oi++)
                    Check(Screens.QuizFeedback(l, qi, oi), $"{l.Id}-dars javob {qi + 1}.{oi + 1}");
            }

            Check(Screens.LessonDone(l, user, l.Quiz.Count, l.Quiz.Count, true), $"{l.Id}-dars yakuni");

            var w = l.Words[0];
            Check(Screens.ReviewQuestion(w, 0, 5), $"{l.Id}-dars takrorlash savoli");
            Check(Screens.ReviewFeedback(w, false, "boshqa"), $"{l.Id}-dars takrorlash javobi");
        }
    }

    // ----------------------------------------------------------- scheduler

    private static void CheckScheduler(LessonStore lessons, List<string> errors)
    {
        var user = new UserState { ChatId = 1 };
        var day1 = new DateTime(2026, 8, 21, 20, 0, 0);

        Leitner.Seed(user, lessons.WordIdsFor(1), day1);
        if (user.Cards.Count != lessons.Get(1)!.Words.Count)
            errors.Add($"SRS: seed {user.Cards.Count} ta karta yaratdi, {lessons.Get(1)!.Words.Count} kutilgan edi.");

        if (Leitner.Due(user, day1).Count == 0)
            errors.Add("SRS: yangi kartalar birinchi kuni takrorlashga chiqmadi.");

        // A correct answer must push the card into the future.
        var id = lessons.Get(1)!.WordId(0);
        Leitner.Record(user, id, correct: true, day1);
        if (string.CompareOrdinal(user.Cards[id].Due, Leitner.Today(day1)) <= 0)
            errors.Add("SRS: toʻgʻri javobdan keyin karta hali ham bugungi kunga tegishli.");

        // A miss must drop it back to box 0 and return it soon.
        Leitner.Record(user, id, correct: false, day1);
        if (user.Cards[id].Box != 0)
            errors.Add($"SRS: xatodan keyin quti {user.Cards[id].Box}, 0 boʻlishi kerak.");

        // Climbing all the boxes must not overflow the interval table.
        for (var i = 0; i < 20; i++) Leitner.Record(user, id, correct: true, day1);
        if (user.Cards[id].Box != Leitner.MaxBox)
            errors.Add($"SRS: eng yuqori quti {user.Cards[id].Box}, {Leitner.MaxBox} kutilgan edi.");

        // Word ids must resolve back to real words.
        foreach (var l in lessons.All)
            foreach (var wid in lessons.WordIdsFor(l.Id))
                if (lessons.GetWord(wid) is null)
                    errors.Add($"SRS: «{wid}» id boʻyicha soʻz topilmadi.");
    }
}
