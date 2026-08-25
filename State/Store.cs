using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishBot.State;

public sealed class LessonResult
{
    public int Score { get; set; }
    public int Total { get; set; }
    public string Date { get; set; } = "";
}

/// <summary>One vocabulary card in the Leitner deck.</summary>
public sealed class Card
{
    public int Box { get; set; }
    public string Due { get; set; } = "";   // local yyyy-MM-dd
    public int Hits { get; set; }
    public int Misses { get; set; }
}

/// <summary>An in-flight lesson quiz. Survives restarts because it is persisted.</summary>
public sealed class QuizRun
{
    public int LessonId { get; set; }
    public int Correct { get; set; }
    public int Answered { get; set; }
}

/// <summary>An in-flight review session over a snapshot of the due deck.</summary>
public sealed class ReviewRun
{
    public List<string> Queue { get; set; } = new();
    public int Index { get; set; }
    public int Correct { get; set; }

    /// <summary>
    /// Word ids backing the four answer buttons of the current question, in button
    /// order. Persisted so the options survive a restart mid-question and the
    /// callback only has to carry an index.
    /// </summary>
    public List<string> Options { get; set; } = new();
}

public sealed class UserState
{
    public long ChatId { get; set; }
    public string Name { get; set; } = "";
    public int RemindHour { get; set; } = 20;
    public bool RemindersOn { get; set; } = true;

    public string? LastStudyDate { get; set; }
    public string? LastNudgeDate { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public int CurrentLesson { get; set; } = 1;

    public Dictionary<int, LessonResult> Lessons { get; set; } = new();
    public Dictionary<string, Card> Cards { get; set; } = new();

    public QuizRun? Quiz { get; set; }
    public ReviewRun? Review { get; set; }

    [JsonIgnore] public int FlowersEarned => Lessons.Count;
}

/// <summary>
/// The whole bot's state in one JSON file. At one or two learners there is no
/// concurrency to speak of, and a file you can open and hand-edit is a feature:
/// bump her to lesson 5 by changing a number. Swap for SQLite if this ever
/// grows past a handful of users.
/// </summary>
public sealed class Store
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<long, UserState> _users;

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Store(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "state.json");

        if (File.Exists(_path))
        {
            try
            {
                _users = JsonSerializer.Deserialize<Dictionary<long, UserState>>(File.ReadAllText(_path), Opts)
                         ?? new Dictionary<long, UserState>();
            }
            catch (JsonException ex)
            {
                var backup = _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(_path, backup);
                Console.Error.WriteLine($"[state] Fayl buzuq ({ex.Message}). Zaxira: {backup}. Yangisidan boshlandi.");
                _users = new Dictionary<long, UserState>();
            }
        }
        else
        {
            _users = new Dictionary<long, UserState>();
        }
    }

    public UserState GetOrCreate(long chatId, string name)
    {
        lock (_users)
        {
            if (!_users.TryGetValue(chatId, out var u))
            {
                u = new UserState { ChatId = chatId, Name = name };
                _users[chatId] = u;
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                u.Name = name;
            }
            return u;
        }
    }

    public IReadOnlyList<UserState> Everyone()
    {
        lock (_users) return _users.Values.ToList();
    }

    /// <summary>Atomic write: temp file then replace, so a crash can't truncate state.</summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            string json;
            lock (_users) json = JsonSerializer.Serialize(_users, Opts);

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);

            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        finally
        {
            _gate.Release();
        }
    }
}
