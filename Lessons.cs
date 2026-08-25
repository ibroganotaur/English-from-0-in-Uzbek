using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnglishBot.Content;

public sealed class Word
{
    public string En { get; set; } = "";
    public string Pron { get; set; } = "";
    public string Uz { get; set; } = "";

    /// <summary>Text actually sent to the speech engine (strips "a / b" alternatives).</summary>
    public string Speakable => En.Split('/')[0].Trim();
}

public sealed class Example
{
    public string En { get; set; } = "";
    public string Uz { get; set; } = "";
}

public sealed class Rule
{
    public string Label { get; set; } = "Qoida";
    public string Body { get; set; } = "";
    public List<Example> Examples { get; set; } = new();
}

public sealed class Pair
{
    public string Bad { get; set; } = "";
    public string Good { get; set; } = "";
}

public sealed class Warning
{
    public string Body { get; set; } = "";
    public List<Pair> Pairs { get; set; } = new();
}

public sealed class Option
{
    public string Text { get; set; } = "";
    public bool Correct { get; set; }

    /// <summary>
    /// Why this wrong answer is wrong, in Uzbek. Every distractor in this course is a
    /// real Uzbek-to-English interference error, so the explanation teaches something.
    /// </summary>
    public string? Why { get; set; }
}

public sealed class Quiz
{
    public string Prompt { get; set; } = "";
    public string Task { get; set; } = "Inglizchaga oʻgiring:";
    public List<Option> Options { get; set; } = new();

    public int CorrectIndex => Options.FindIndex(o => o.Correct);
    public string CorrectText => Options.FirstOrDefault(o => o.Correct)?.Text ?? "";
}

public sealed class Lesson
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Flower { get; set; } = "🌸";
    public List<Word> Words { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    public List<Example> Examples { get; set; } = new();
    public Warning? Warning { get; set; }
    public List<Quiz> Quiz { get; set; } = new();

    public string WordId(int index) => $"{Id}-{index}";
}

/// <summary>Loads Content/lessons/*.json, sorted by file name.</summary>
public sealed class LessonStore
{
    public IReadOnlyList<Lesson> All { get; }

    private readonly Dictionary<int, Lesson> _byId;

    private LessonStore(List<Lesson> lessons)
    {
        All = lessons;
        _byId = lessons.ToDictionary(l => l.Id);
    }

    public int Count => All.Count;

    public Lesson? Get(int id) => _byId.TryGetValue(id, out var l) ? l : null;

    public Word? GetWord(string wordId)
    {
        var parts = wordId.Split('-');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var lid) || !int.TryParse(parts[1], out var wi)) return null;
        var lesson = Get(lid);
        if (lesson is null || wi < 0 || wi >= lesson.Words.Count) return null;
        return lesson.Words[wi];
    }

    /// <summary>All word ids belonging to a lesson, used to seed the review deck.</summary>
    public IEnumerable<string> WordIdsFor(int lessonId)
    {
        var lesson = Get(lessonId);
        if (lesson is null) yield break;
        for (var i = 0; i < lesson.Words.Count; i++) yield return lesson.WordId(i);
    }

    /// <summary>
    /// The word ids a question actually exercises — the lesson's words that appear in its
    /// correct answer. The quiz JSON carries no word references, so the sentence itself is
    /// the link: "I am a doctor." exercises I, am and doctor, and nothing else. This is what
    /// a missed question should blame, so the review deck drills what she really got wrong.
    /// </summary>
    public IEnumerable<string> WordIdsInQuestion(int lessonId, int questionIndex)
    {
        var lesson = Get(lessonId);
        if (lesson is null || questionIndex < 0 || questionIndex >= lesson.Quiz.Count) yield break;

        var text = lesson.Quiz[questionIndex].CorrectText;
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var hits = WordsIn(lesson, text).ToList();

        // Later lessons recycle earlier vocabulary — "My name is Aziza." in lesson 10 uses
        // no lesson-10 word at all. Widening to the whole course stops those questions from
        // blaming nothing. Only as a fallback, so an everyday word like "a" is not charged
        // with every miss in the course.
        if (hits.Count == 0)
            hits = All.SelectMany(l => WordsIn(l, text)).ToList();

        foreach (var id in hits) yield return id;
    }

    /// <summary>The lesson's words that literally appear in a sentence.</summary>
    private static IEnumerable<string> WordsIn(Lesson lesson, string text)
    {
        for (var i = 0; i < lesson.Words.Count; i++)
        {
            // "a / an" style entries list alternatives; any of them counts as a hit.
            foreach (var variant in lesson.Words[i].En.Split('/'))
            {
                var w = variant.Trim();
                if (w.Length == 0) continue;

                // Not \b: these are whole words and short phrases sitting next to punctuation.
                if (Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(w)}(?!\w)", RegexOptions.IgnoreCase))
                {
                    yield return lesson.WordId(i);
                    break;
                }
            }
        }
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static LessonStore Load(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Darslar papkasi topilmadi: {dir}");

        var lessons = new List<Lesson>();

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var lesson = JsonSerializer.Deserialize<Lesson>(File.ReadAllText(file), Opts);
                if (lesson is not null) lessons.Add(lesson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{Path.GetFileName(file)} buzuq: {ex.Message}", ex);
            }
        }

        if (lessons.Count == 0)
            throw new InvalidOperationException($"{dir} ichida birorta ham dars yoʻq.");

        return new LessonStore(lessons.OrderBy(l => l.Id).ToList());
    }
}
