using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishBot;

/// <summary>
/// Settings, merged from appsettings.json then appsettings.Local.json (local wins),
/// then environment variables (which win over both, for VPS deployment).
/// </summary>
public sealed class BotConfig
{
    public string Token { get; set; } = "";
    public long[] AllowedUserIds { get; set; } = Array.Empty<long>();
    public int TzOffsetMinutes { get; set; } = 300;
    public int RemindHour { get; set; } = 20;

    public string AzureSpeechKey { get; set; } = "";
    public string AzureSpeechRegion { get; set; } = "westeurope";
    public string AzureVoice { get; set; } = "en-US-AriaNeural";

    /// <summary>
    /// Optional override for where progress is stored. Leave it unset and the data lands
    /// outside the build output, so a rebuild, a `dotnet clean`, or a publish to a fresh
    /// folder can never take her streak and her garden with it.
    /// </summary>
    public string? DataPath { get; set; }

    [JsonIgnore] public string RootDir { get; set; } = "";
    [JsonIgnore] public string DataDir => ResolveDataDir();

    private string ResolveDataDir()
    {
        var env = Environment.GetEnvironmentVariable("ENGLISHBOT_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (!string.IsNullOrWhiteSpace(DataPath)) return DataPath.Trim();

        // %LOCALAPPDATA%\EnglishBot on Windows, ~/.local/share/EnglishBot on Linux.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local)
            ? Path.Combine(RootDir, "data")
            : Path.Combine(local, "EnglishBot");
    }
    [JsonIgnore] public string AudioDir => Path.Combine(DataDir, "audio");
    [JsonIgnore] public string LessonsDir => Path.Combine(RootDir, "Content", "lessons");

    [JsonIgnore] public TimeSpan TzOffset => TimeSpan.FromMinutes(TzOffsetMinutes);

    /// <summary>Her local "now" — the bot thinks in her clock, not the server's.</summary>
    public DateTime LocalNow() => DateTime.UtcNow + TzOffset;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static BotConfig Load(string rootDir)
    {
        var cfg = ReadFile(Path.Combine(rootDir, "appsettings.json")) ?? new BotConfig();
        var local = ReadFile(Path.Combine(rootDir, "appsettings.Local.json"));

        if (local is not null)
        {
            if (!string.IsNullOrWhiteSpace(local.Token)) cfg.Token = local.Token;
            if (local.AllowedUserIds.Length > 0) cfg.AllowedUserIds = local.AllowedUserIds;
            if (local.TzOffsetMinutes != 300) cfg.TzOffsetMinutes = local.TzOffsetMinutes;
            if (local.RemindHour != 20) cfg.RemindHour = local.RemindHour;
            if (!string.IsNullOrWhiteSpace(local.AzureSpeechKey)) cfg.AzureSpeechKey = local.AzureSpeechKey;
            if (!string.IsNullOrWhiteSpace(local.AzureSpeechRegion)) cfg.AzureSpeechRegion = local.AzureSpeechRegion;
            if (!string.IsNullOrWhiteSpace(local.AzureVoice)) cfg.AzureVoice = local.AzureVoice;
            if (!string.IsNullOrWhiteSpace(local.DataPath)) cfg.DataPath = local.DataPath;
        }

        // Environment wins — this is how you configure it on a server.
        var envToken = Environment.GetEnvironmentVariable("ENGLISHBOT_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken)) cfg.Token = envToken.Trim();

        var envKey = Environment.GetEnvironmentVariable("ENGLISHBOT_AZURE_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) cfg.AzureSpeechKey = envKey.Trim();

        var envRegion = Environment.GetEnvironmentVariable("ENGLISHBOT_AZURE_REGION");
        if (!string.IsNullOrWhiteSpace(envRegion)) cfg.AzureSpeechRegion = envRegion.Trim();

        cfg.RootDir = rootDir;
        return cfg;
    }

    private static BotConfig? ReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<BotConfig>(File.ReadAllText(path), Opts);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[config] {Path.GetFileName(path)} oʻqib boʻlmadi: {ex.Message}");
            return null;
        }
    }

    public bool TokenLooksReal =>
        !string.IsNullOrWhiteSpace(Token)
        && !Token.StartsWith("PASTE", StringComparison.OrdinalIgnoreCase)
        && Token.Contains(':');

    public bool IsAllowed(long userId) =>
        AllowedUserIds.Length == 0 || Array.IndexOf(AllowedUserIds, userId) >= 0;

    public bool TtsEnabled => !string.IsNullOrWhiteSpace(AzureSpeechKey);
}
