using System.Net;
using System.Text;
using EnglishBot.Content;
using EnglishBot.Srs;
using EnglishBot.State;
using EnglishBot.Telegram;

namespace EnglishBot.Ui;

/// <summary>
/// The look of the bot. Flowers are not decoration here — they are the progress
/// system. Every finished lesson blooms one flower in her garden, and the daily
/// streak is a plant that grows the longer she keeps it alive. Nothing ever wilts:
/// missing a day slows growth, it does not destroy anything.
/// </summary>
public static class Deco
{
    public const string Line = "❀ ─────────────── ❀";

    public static string Head(string text) => $"✿ ── {text} ── ✿";

    /// <summary>Streak rendered as a plant at the stage it has earned.</summary>
    public static string Plant(int streak) => streak switch
    {
        <= 0 => "🌱",
        < 3 => "🌱",
        < 7 => "🌿",
        < 14 => "🌷",
        < 30 => "🌸",
        < 60 => "🌺",
        _ => "🌳"
    };

    public static string PlantName(int streak) => streak switch
    {
        <= 0 => "urugʻ",
        < 3 => "nihol",
        < 7 => "novda",
        < 14 => "lolaqizgʻaldoq",
        < 30 => "gullagan",
        < 60 => "toʻliq ochilgan",
        _ => "daraxt"
    };

    /// <summary>Score as a row of blooms; unearned slots stay as seedlings.</summary>
    public static string Bar(int done, int total)
    {
        if (total <= 0) return "";
        var sb = new StringBuilder(total * 2);
        for (var i = 0; i < total; i++) sb.Append(i < done ? "🌸" : "🌱");
        return sb.ToString();
    }

    /// <summary>The garden: earned flowers laid out five to a row.</summary>
    public static string Garden(IEnumerable<string> flowers)
    {
        var list = flowers.ToList();
        if (list.Count == 0) return "<i>Bogʻ hali boʻsh. Birinchi darsni tugating — birinchi gul ochiladi.</i>";

        var sb = new StringBuilder();
        for (var i = 0; i < list.Count; i++)
        {
            sb.Append(list[i]);
            if ((i + 1) % 5 == 0 && i != list.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");
}

public static class Screens
{
    // ---------------------------------------------------------------- welcome

    public static string Welcome(string name) => $"""
        🌷 <b>Noldan Ingliz Tili</b>

        Assalomu alaykum{(string.IsNullOrWhiteSpace(name) ? "" : ", " + Deco.Esc(name))}!

        Bu yerda ingliz tilini <b>noldan</b> oʻrganamiz. Hamma qoida oʻzbek tilida tushuntiriladi — yodlab emas, <b>tushunib</b> oʻrganasiz.

        {Deco.Line}

        Kuniga atigi <b>20 daqiqa</b>. Har bir tugallangan dars — bogʻingizda bitta yangi gul. 🌱

        Boshlaymizmi?
        """;

    // ------------------------------------------------------------------- home

    public static string Home(UserState u, LessonStore lessons, int due, DateTime localNow)
    {
        var next = lessons.Get(u.CurrentLesson);
        var sb = new StringBuilder();

        sb.AppendLine("🌷 <b>Noldan Ingliz Tili</b>");
        sb.AppendLine();
        sb.AppendLine($"{Deco.Plant(u.Streak)} Ketma-ket kunlar: <b>{u.Streak}</b>");
        sb.AppendLine($"🌸 Bogʻim: <b>{u.FlowersEarned}</b> ta gul");

        if (next is not null)
            sb.AppendLine($"📖 Keyingi: <b>{next.Id}-dars</b> · {Deco.Esc(next.Title)}");
        else
            sb.AppendLine("🌳 Hamma darslar tugallandi!");

        if (due > 0)
            sb.AppendLine($"🔁 Takrorlash kerak: <b>{due}</b> ta soʻz");

        sb.AppendLine();
        sb.AppendLine(Deco.Line);

        if (u.LastStudyDate == Leitner.Today(localNow))
        {
            sb.AppendLine();
            sb.AppendLine("✅ <i>Bugungi dars bajarildi. Zoʻr!</i>");
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------- lesson cards

    /// <summary>Number of teaching cards before the quiz starts.</summary>
    public static int StepCount(Lesson l) =>
        1                                        // words
        + l.Rules.Count
        + (l.Examples.Count > 0 ? 1 : 0)
        + (l.Warning is not null ? 1 : 0);

    public static string Step(Lesson l, int step)
    {
        if (step == 0) return Words(l);

        var i = step - 1;
        if (i < l.Rules.Count) return RuleCard(l, l.Rules[i], i + 1, l.Rules.Count);

        i -= l.Rules.Count;
        if (l.Examples.Count > 0)
        {
            if (i == 0) return ExamplesCard(l);
            i--;
        }

        if (l.Warning is not null && i == 0) return WarningCard(l);

        return Words(l);   // defensive: never leave her on a blank card
    }

    private static string Header(Lesson l) =>
        $"{l.Flower} <b>{l.Id}-dars · {Deco.Esc(l.Title)}</b>\n<i>{Deco.Esc(l.Subtitle)}</i>";

    private static string Words(Lesson l)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header(l));
        sb.AppendLine();
        sb.AppendLine(Deco.Head("Soʻzlar"));
        sb.AppendLine();

        foreach (var w in l.Words)
        {
            sb.AppendLine($"<b>{Deco.Esc(w.En)}</b>  ·  <code>{Deco.Esc(w.Pron)}</code>");
            sb.AppendLine($"<i>{Deco.Esc(w.Uz)}</i>");
            sb.AppendLine();
        }

        sb.AppendLine("<i>KATTA HARF — urgʻu tushadigan boʻgʻin.</i>");
        return sb.ToString().TrimEnd();
    }

    private static string RuleCard(Lesson l, Rule r, int n, int total)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📘 <b>{Deco.Esc(r.Label)}</b>{(total > 1 ? $"  <i>({n}/{total})</i>" : "")}");
        sb.AppendLine();
        sb.AppendLine(r.Body);

        if (r.Examples.Count > 0)
        {
            sb.AppendLine();
            foreach (var e in r.Examples)
            {
                sb.AppendLine($"<i>{Deco.Esc(e.Uz)}</i>");
                sb.AppendLine($"→ <b>{Deco.Esc(e.En)}</b>");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExamplesCard(Lesson l)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Deco.Head("Namuna gaplar"));
        sb.AppendLine();
        foreach (var e in l.Examples)
        {
            sb.AppendLine($"<b>{Deco.Esc(e.En)}</b>");
            sb.AppendLine($"<i>{Deco.Esc(e.Uz)}</i>");
            sb.AppendLine();
        }
        sb.AppendLine("<i>Har birini ovoz chiqarib ayting.</i>");
        return sb.ToString().TrimEnd();
    }

    private static string WarningCard(Lesson l)
    {
        var w = l.Warning!;
        var sb = new StringBuilder();
        sb.AppendLine("🌺 <b>Diqqat</b>");
        sb.AppendLine();
        sb.AppendLine(w.Body);

        if (w.Pairs.Count > 0) sb.AppendLine();

        foreach (var p in w.Pairs)
        {
            sb.AppendLine($"❌ <s>{Deco.Esc(p.Bad)}</s>");
            sb.AppendLine($"✅ <b>{Deco.Esc(p.Good)}</b>");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ quiz

    public static string QuizQuestion(Lesson l, int qi)
    {
        var q = l.Quiz[qi];
        return $"""
            {Deco.Head($"Mashq {qi + 1}/{l.Quiz.Count}")}

            {Deco.Esc(q.Task)}

            <b>{Deco.Esc(q.Prompt)}</b>
            """;
    }

    public static string QuizFeedback(Lesson l, int qi, int chosen)
    {
        var q = l.Quiz[qi];
        var opt = q.Options[chosen];
        var sb = new StringBuilder();

        if (opt.Correct)
        {
            sb.AppendLine("✅ <b>Toʻgʻri!</b>");
            sb.AppendLine();
            sb.AppendLine($"<b>{Deco.Esc(q.CorrectText)}</b>");
        }
        else
        {
            sb.AppendLine("❌ <b>Notoʻgʻri</b>");
            sb.AppendLine();
            sb.AppendLine($"Siz tanladingiz: <s>{Deco.Esc(opt.Text)}</s>");
            if (!string.IsNullOrWhiteSpace(opt.Why))
            {
                sb.AppendLine();
                sb.AppendLine($"<i>{opt.Why}</i>");
            }
            sb.AppendLine();
            sb.AppendLine($"✅ Toʻgʻri javob: <b>{Deco.Esc(q.CorrectText)}</b>");
        }

        return sb.ToString().TrimEnd();
    }

    public static string LessonDone(Lesson l, UserState u, int score, int total, bool firstTime)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🌸 <b>{l.Id}-dars tugadi!</b>");
        sb.AppendLine();
        sb.AppendLine($"Natija: <b>{score}/{total}</b>");
        sb.AppendLine(Deco.Bar(score, total));
        sb.AppendLine();

        if (firstTime)
        {
            sb.AppendLine($"Bogʻingizda yangi gul ochildi:  {l.Flower}");
            sb.AppendLine();
        }

        sb.AppendLine(Deco.Line);
        sb.AppendLine();
        sb.AppendLine($"{Deco.Plant(u.Streak)} Ketma-ket kunlar: <b>{u.Streak}</b>");

        if (score < total)
            sb.AppendLine("\n<i>Xato qilgan soʻzlaringiz takrorlash roʻyxatiga qoʻshildi.</i>");

        return sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------- garden

    public static string Garden(UserState u, LessonStore lessons, DateTime localNow)
    {
        var flowers = lessons.All
            .Where(l => u.Lessons.ContainsKey(l.Id))
            .Select(l => l.Flower);

        var sb = new StringBuilder();
        sb.AppendLine("🌸 <b>Mening bogʻim</b>");
        sb.AppendLine();
        sb.AppendLine(Deco.Garden(flowers));
        sb.AppendLine();
        sb.AppendLine($"<i>{lessons.Count} ta darsdan {u.FlowersEarned} tasi tugallandi.</i>");
        sb.AppendLine();
        sb.AppendLine(Deco.Line);
        sb.AppendLine();
        sb.AppendLine($"{Deco.Plant(u.Streak)} Hozirgi ketma-ketlik: <b>{u.Streak}</b> kun  <i>({Deco.PlantName(u.Streak)})</i>");
        sb.AppendLine($"🏆 Eng uzun: <b>{u.BestStreak}</b> kun");
        sb.AppendLine($"📚 Oʻrganilayotgan soʻzlar: <b>{u.Cards.Count}</b>");
        sb.AppendLine($"🌳 Mustahkam yodlangan: <b>{Leitner.Mastered(u)}</b>");
        sb.AppendLine($"🔁 Bugun takrorlash: <b>{Leitner.DueCount(u, localNow)}</b>");

        return sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------- review

    public static string ReviewQuestion(Word w, int index, int total) => $"""
        {Deco.Head($"Takrorlash {index + 1}/{total}")}

        <b>{Deco.Esc(w.En)}</b>  ·  <code>{Deco.Esc(w.Pron)}</code>

        Bu nima?
        """;

    public static string ReviewFeedback(Word w, bool correct, string chosen)
    {
        var sb = new StringBuilder();
        if (correct)
        {
            sb.AppendLine("✅ <b>Toʻgʻri!</b>");
        }
        else
        {
            sb.AppendLine("❌ <b>Notoʻgʻri</b>");
            sb.AppendLine();
            sb.AppendLine($"Siz tanladingiz: <s>{Deco.Esc(chosen)}</s>");
        }
        sb.AppendLine();
        sb.AppendLine($"<b>{Deco.Esc(w.En)}</b> — <i>{Deco.Esc(w.Uz)}</i>");
        return sb.ToString().TrimEnd();
    }

    public static string ReviewDone(int correct, int total) => $"""
        🌿 <b>Takrorlash tugadi</b>

        Natija: <b>{correct}/{total}</b>
        {Deco.Bar(correct, total)}

        {Deco.Line}

        <i>Bilmagan soʻzlaringiz ertaga yana chiqadi. Bilganlaringiz esa keyinroq — shu tarzda xotirada mustahkamlanadi.</i>
        """;

    // -------------------------------------------------------------- settings

    public static string Settings(UserState u) => $"""
        ⚙️ <b>Sozlamalar</b>

        ⏰ Kunlik eslatma: <b>{(u.RemindersOn ? $"{u.RemindHour:00}:00" : "oʻchirilgan")}</b>
        📖 Hozirgi dars: <b>{u.CurrentLesson}</b>

        {Deco.Line}

        <i>Eslatma har kuni bir xil vaqtda keladi. Eng yaxshisi — kechki ovqatdan keyingi tinch payt.</i>
        """;

    // ----------------------------------------------------------------- nudge

    public static string Nudge(UserState u, Lesson? next)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Deco.Plant(u.Streak)} <b>Ingliz tili vaqti</b>");
        sb.AppendLine();

        if (u.Streak > 0)
            sb.AppendLine($"Ketma-ket <b>{u.Streak}</b> kun. Bugun ham davom ettiramizmi?");
        else
            sb.AppendLine("Bugun 20 daqiqa ajratamizmi?");

        if (next is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"📖 <b>{next.Id}-dars</b> · {Deco.Esc(next.Title)}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string AllDone() => $"""
        🌳 <b>Barcha darslar tugallandi!</b>

        Siz 1-blokni toʻliq bitirdingiz — bu <b>A1</b> darajaning asosi.

        {Deco.Line}

        Endi qilish kerak boʻlgan narsa: har kuni soʻzlarni takrorlashda davom eting. Keyingi blok (Present Simple, can, oʻtgan zamon) tayyor boʻlganda shu yerda paydo boʻladi.
        """;
}

/// <summary>Inline keyboards. Callback data is kept short — Telegram caps it at 64 bytes.</summary>
public static class Kb
{
    public static IEnumerable<IEnumerable<Btn>> Rows(params IEnumerable<Btn>[] rows) => rows;

    public static IEnumerable<Btn> Row(params Btn[] buttons) => buttons;

    public static IEnumerable<IEnumerable<Btn>> Welcome() =>
        Rows(Row(Btn.Of("🌸 Boshlash", "home")));

    public static IEnumerable<IEnumerable<Btn>> Home(UserState u, LessonStore lessons, int due)
    {
        var rows = new List<IEnumerable<Btn>>();
        var next = lessons.Get(u.CurrentLesson);

        if (next is not null)
            rows.Add(Row(Btn.Of($"📖 {next.Id}-darsni boshlash", $"lesson:{next.Id}:0")));

        if (due > 0)
            rows.Add(Row(Btn.Of($"🔁 Soʻzlarni takrorlash ({due})", "review")));

        rows.Add(Row(Btn.Of("🌸 Bogʻim", "garden"), Btn.Of("⚙️ Sozlamalar", "settings")));

        if (u.FlowersEarned > 0)
            rows.Add(Row(Btn.Of("📚 Oʻtilgan darslar", "list")));

        return rows;
    }

    /// <summary>
    /// Words card. Each word is a tap-to-hear button when audio is on; without a
    /// speech key those buttons are simply absent.
    /// </summary>
    public static IEnumerable<IEnumerable<Btn>> LessonWords(Lesson l, bool audio)
    {
        var rows = new List<IEnumerable<Btn>>();

        if (audio)
        {
            for (var i = 0; i < l.Words.Count; i += 2)
            {
                var row = new List<Btn> { Btn.Of($"🔊 {l.Words[i].En}", $"say:{l.Id}:{i}") };
                if (i + 1 < l.Words.Count)
                    row.Add(Btn.Of($"🔊 {l.Words[i + 1].En}", $"say:{l.Id}:{i + 1}"));
                rows.Add(row);
            }
        }

        rows.Add(Row(Btn.Of("Davom etish →", $"lesson:{l.Id}:1")));
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> LessonStep(Lesson l, int step)
    {
        var last = StepIsLast(l, step);
        var rows = new List<IEnumerable<Btn>>();

        var nav = new List<Btn>();
        if (step > 0) nav.Add(Btn.Of("← Orqaga", $"lesson:{l.Id}:{step - 1}"));
        nav.Add(last
            ? Btn.Of("✍️ Mashqqa oʻtish", $"quiz:{l.Id}:0")
            : Btn.Of("Davom etish →", $"lesson:{l.Id}:{step + 1}"));
        rows.Add(nav);

        rows.Add(Row(Btn.Of("🏠 Bosh sahifa", "home")));
        return rows;
    }

    public static bool StepIsLast(Lesson l, int step) => step >= Screens.StepCount(l) - 1;

    public static IEnumerable<IEnumerable<Btn>> QuizOptions(Lesson l, int qi)
    {
        var q = l.Quiz[qi];
        var rows = new List<IEnumerable<Btn>>();
        for (var i = 0; i < q.Options.Count; i++)
            rows.Add(Row(Btn.Of(q.Options[i].Text, $"ans:{l.Id}:{qi}:{i}")));
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> QuizNext(Lesson l, int qi)
    {
        var isLast = qi + 1 >= l.Quiz.Count;
        return Rows(Row(isLast
            ? Btn.Of("🌸 Yakunlash", $"done:{l.Id}")
            : Btn.Of("Keyingi →", $"quiz:{l.Id}:{qi + 1}")));
    }

    public static IEnumerable<IEnumerable<Btn>> AfterLesson(LessonStore lessons, UserState u, int due)
    {
        var rows = new List<IEnumerable<Btn>>();
        if (due > 0) rows.Add(Row(Btn.Of($"🔁 Soʻzlarni takrorlash ({due})", "review")));
        rows.Add(Row(Btn.Of("🌸 Bogʻim", "garden"), Btn.Of("🏠 Bosh sahifa", "home")));
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> ReviewOptions(IReadOnlyList<string> labels)
    {
        var rows = new List<IEnumerable<Btn>>();
        for (var i = 0; i < labels.Count; i++)
            rows.Add(Row(Btn.Of(labels[i], $"rans:{i}")));
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> ReviewNext() =>
        Rows(Row(Btn.Of("Keyingi →", "rnext")));

    public static IEnumerable<IEnumerable<Btn>> Back() =>
        Rows(Row(Btn.Of("🏠 Bosh sahifa", "home")));

    public static IEnumerable<IEnumerable<Btn>> Settings(UserState u)
    {
        var rows = new List<IEnumerable<Btn>>
        {
            Row(Btn.Of("🕗 18:00", "hour:18"), Btn.Of("🕘 19:00", "hour:19")),
            Row(Btn.Of("🕙 20:00", "hour:20"), Btn.Of("🕚 21:00", "hour:21")),
            Row(Btn.Of(u.RemindersOn ? "🔕 Eslatmani oʻchirish" : "🔔 Eslatmani yoqish",
                       u.RemindersOn ? "remind:0" : "remind:1")),
            Row(Btn.Of("🏠 Bosh sahifa", "home"))
        };
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> LessonList(LessonStore lessons, UserState u)
    {
        var rows = new List<IEnumerable<Btn>>();
        foreach (var l in lessons.All)
        {
            var done = u.Lessons.ContainsKey(l.Id);
            var mark = done ? l.Flower : "🌱";
            rows.Add(Row(Btn.Of($"{mark}  {l.Id}. {l.Title}", $"lesson:{l.Id}:0")));
        }
        rows.Add(Row(Btn.Of("🏠 Bosh sahifa", "home")));
        return rows;
    }

    public static IEnumerable<IEnumerable<Btn>> Nudge(Lesson? next) =>
        Rows(Row(next is not null
            ? Btn.Of($"📖 {next.Id}-darsni boshlash", $"lesson:{next.Id}:0")
            : Btn.Of("🔁 Takrorlash", "review")));
}
