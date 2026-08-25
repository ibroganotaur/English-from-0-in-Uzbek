using EnglishBot.State;

namespace EnglishBot.Srs;

/// <summary>
/// Leitner box scheduling. A word she gets right moves up a box and comes back
/// later; a word she misses drops to box 0 and comes back tomorrow. The point is
/// that the deck concentrates on exactly the words she keeps forgetting.
/// </summary>
public static class Leitner
{
    /// <summary>Days until a card in box N is due again.</summary>
    private static readonly int[] IntervalDays = { 1, 2, 4, 8, 16, 32 };

    public const int MaxBox = 5;

    public static string Today(DateTime localNow) => localNow.ToString("yyyy-MM-dd");

    public static void Seed(UserState user, IEnumerable<string> wordIds, DateTime localNow)
    {
        var today = Today(localNow);
        foreach (var id in wordIds)
        {
            if (user.Cards.ContainsKey(id)) continue;
            user.Cards[id] = new Card { Box = 0, Due = today };
        }
    }

    public static void Record(UserState user, string wordId, bool correct, DateTime localNow)
    {
        if (!user.Cards.TryGetValue(wordId, out var card))
        {
            card = new Card();
            user.Cards[wordId] = card;
        }

        if (correct)
        {
            card.Hits++;
            card.Box = Math.Min(card.Box + 1, MaxBox);
        }
        else
        {
            card.Misses++;
            card.Box = 0;
        }

        card.Due = localNow.AddDays(IntervalDays[card.Box]).ToString("yyyy-MM-dd");
    }

    /// <summary>Cards due on or before today, weakest (most-missed, lowest box) first.</summary>
    public static List<string> Due(UserState user, DateTime localNow, int limit = 10)
    {
        var today = Today(localNow);

        return user.Cards
            .Where(kv => string.CompareOrdinal(kv.Value.Due, today) <= 0)
            .OrderBy(kv => kv.Value.Box)
            .ThenByDescending(kv => kv.Value.Misses)
            .Select(kv => kv.Key)
            .Take(limit)
            .ToList();
    }

    public static int DueCount(UserState user, DateTime localNow)
    {
        var today = Today(localNow);
        return user.Cards.Count(kv => string.CompareOrdinal(kv.Value.Due, today) <= 0);
    }

    /// <summary>Cards in the top box — treated as "known".</summary>
    public static int Mastered(UserState user) => user.Cards.Count(kv => kv.Value.Box >= MaxBox);
}
