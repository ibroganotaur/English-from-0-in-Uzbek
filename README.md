# 🌷 Noldan Ingliz Tili — Telegram bot

Uzbek-language English course for absolute beginners, delivered as a Telegram bot.
Companion to the web course; this half handles the thing a web page cannot: showing
up every evening, speaking the words out loud, and drilling exactly the vocabulary
she keeps forgetting.

**No NuGet packages.** Everything used ships in the .NET 8 shared framework, so there
is nothing to restore and nothing to break. It runs the same on Windows and on a Linux VPS.

---

## Setup (about five minutes)

### 1. Create the bot

In Telegram, message **@BotFather**:

```
/newbot
```

Give it a display name (e.g. `Noldan Ingliz Tili`) and a username ending in `bot`.
BotFather replies with a token that looks like `1234567890:AAH...`.

### 2. Give the bot its token

Create `appsettings.Local.json` next to `appsettings.json`:

```json
{
  "token": "1234567890:AAH..."
}
```

That file is git-ignored. Never put the token in `appsettings.json`.

On a server, use the environment variable instead:

```bash
export ENGLISHBOT_TOKEN="1234567890:AAH..."
```

### 3. Run it

```bash
dotnet run
```

You should see the bot's `@username` and the lesson count. Open the bot in Telegram
and send `/start`.

### 4. Lock it to your family

Anyone who finds the username can talk to the bot. Send `/whoami` to it from her
phone, then put the number it replies with into `appsettings.Local.json`:

```json
{
  "token": "1234567890:AAH...",
  "allowedUserIds": [123456789]
}
```

---

## Voice notes

**Audio is free and needs no account.** Windows' own speech engine says the word,
VLC converts it to the MP3 Telegram accepts for voice notes, and the clip is cached
forever. This closes the biggest gap in the written course: she was reading `UO-ter`
and guessing at the sound.

Requirements, both of which most Windows machines already have:

- an American English voice — `Microsoft Zira Desktop` ships with Windows
  (add more under *Settings → Time & language → Speech*)
- **VLC** — looked for in both `Program Files` folders, or set `vlcPath`

Tune it in `appsettings.Local.json`:

```json
{
  "windowsVoice": "Microsoft Zira Desktop",
  "speechRate": -2
}
```

`speechRate` runs −10 to 10; negative is slower. −2 is about right for a beginner
trying to imitate the sound.

The startup banner tells you which engine is live:

```
audio      yoqilgan (Windows: Microsoft Zira Desktop)
```

If it says `oʻchirilgan`, the bot simply runs without audio and the 🔊 buttons are
hidden — nothing breaks.

### Optional upgrade: Azure

The Windows voice is robotic. If you want a natural neural voice, create an **Azure
Speech** resource (free F0 tier) and add a key — the bot switches automatically:

```json
{
  "azureSpeechKey": "...",
  "azureSpeechRegion": "westeurope",
  "azureVoice": "en-US-AriaNeural"
}
```

Azure returns OGG/Opus directly, so VLC is not involved. Either way speech is slowed
for a beginner and every clip is cached — each word costs one synthesis, ever.


---

## Checking your content

After editing any lesson file:

```bash
dotnet run -- --check
```

This validates every lesson and renders every screen the bot can produce, without
touching Telegram. It catches:

- quizzes with zero or two correct answers
- distractors missing their Uzbek explanation
- HTML tags Telegram rejects, unbalanced tags, bare `&`
- messages over Telegram's 4096-character limit
- `callback_data` over the 64-byte limit
- button labels too long for a phone screen
- broken word ids and spaced-repetition regressions

---

## What she sees

| Screen | What it does |
|---|---|
| `/start` | Welcome, then the main menu |
| **Bugungi dars** | Words → rules → examples → Diqqat → 6-question quiz |
| **Soʻzlarni takrorlash** | Spaced-repetition drill over words she has missed |
| **Bogʻim** | Her garden, streak, mastered-word count |
| **Sozlamalar** | Reminder time, or turn reminders off |
| *(evening)* | The daily nudge, at her chosen hour |

### The flowers are the progress system

Each finished lesson blooms one distinct flower in her garden 🌷🌸🌺🌻🌼🌹💐🏵️🍀🌳.
The daily streak is a plant that grows the longer she keeps it alive:

```
🌱 nihol (1–2 kun) → 🌿 novda (3–6) → 🌷 (7–13) → 🌸 (14–29) → 🌺 (30–59) → 🌳 daraxt (60+)
```

Nothing ever wilts. Missing days slows growth; it never destroys what she earned.
Breaking a streak resets it to 1, not 0 — coming back today is still a day studied.

### Every wrong answer teaches

Distractors are not random. Each one is a real Uzbek→English interference error, and
choosing it explains the rule in Uzbek:

> **Men shifokorman.**
> - ✅ I am a doctor.
> - ❌ Am a doctor. → *Ega tushib qolgan. Inglizchada «I» har doim yoziladi.*
> - ❌ I am doctor. → *Artikl yoʻq. Kasb oldida «a» shart.*
> - ❌ I doctor am. → *Bu oʻzbekcha soʻz tartibi.*

---

## Editing lessons

`Content/lessons/NN.json`, one file per lesson. Add `11.json` and it appears
automatically — files are loaded in name order and sorted by `id`.

```json
{
  "id": 11,
  "title": "Present Simple",
  "subtitle": "I work, she works",
  "flower": "🌾",
  "words":    [ { "en": "work", "pron": "UOOK", "uz": "ishlamoq" } ],
  "rules":    [ { "label": "Qoida", "body": "...", "examples": [ { "en": "...", "uz": "..." } ] } ],
  "examples": [ { "en": "...", "uz": "..." } ],
  "warning":  { "body": "...", "pairs": [ { "bad": "...", "good": "..." } ] },
  "quiz":     [ {
    "task": "Inglizchaga oʻgiring:",
    "prompt": "Men ishlayman.",
    "options": [
      { "text": "I work.",    "correct": true },
      { "text": "I works.",   "why": "«-s» faqat he / she / it bilan." }
    ]
  } ]
}
```

`body` and `why` accept Telegram HTML — `<b>`, `<i>`, `<code>`, `<s>`, `<u>`. No
`<br>`; use `\n`. Run `--check` afterwards.

---

## State

Everything lives in `state.json` — progress, streaks, the review deck. It is
plain JSON on purpose: to move her to lesson 5, open it and change `currentLesson`.
Writes are atomic (temp file then replace), and a corrupt file is backed up rather
than silently discarded.

It is stored **outside** the build output, so `dotnet clean`, a rebuild, or a publish
to a fresh folder can never destroy her streak:

| OS | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\EnglishBot` |
| Linux | `~/.local/share/EnglishBot` |

Override it with `"dataPath"` in `appsettings.Local.json`, or the `ENGLISHBOT_DATA`
environment variable (which wins over both).

Swap it for SQLite if this ever grows past a handful of learners.

---

## Running it on Windows

```
dotnet publish -c Release -o publish
```

Then double-click **`run-bot.cmd`**. It runs the published build and restarts it ten
seconds after any crash, so a dropped connection does not cost her the evening reminder.

To start it automatically at logon, press <kbd>Win</kbd>+<kbd>R</kbd>, run `shell:startup`,
and drop a shortcut to **`start-minimized.vbs`** in the folder that opens. Delete that
shortcut to undo it.

This survives reboots — but not a sleeping laptop. If the machine is asleep at 20:00,
the reminder does not fire. For a reminder that always arrives, use a VPS.

## Running it for real

On your laptop the bot dies every time the lid closes, which defeats the point of a
daily reminder. For a few euros a month:

```bash
# on the VPS
git clone <your repo> && cd EnglishBot
export ENGLISHBOT_TOKEN="..."
dotnet publish -c Release -o /opt/englishbot
```

`/etc/systemd/system/englishbot.service`:

```ini
[Unit]
Description=Noldan Ingliz Tili bot
After=network-online.target

[Service]
WorkingDirectory=/opt/englishbot
ExecStart=/usr/bin/dotnet /opt/englishbot/EnglishBot.dll
Environment=ENGLISHBOT_TOKEN=1234567890:AAH...
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
systemctl enable --now englishbot
```

Set `tzOffsetMinutes` to `300` (UTC+5) so the reminder fires at her clock, not the
server's — that is already the default.

---

## Layout

```
Program.cs              startup, long-poll loop
BotConfig.cs            settings + env overrides
Telegram/               hand-rolled Bot API client
Content/Lessons.cs      lesson models + loader
Content/lessons/*.json  the course
State/Store.cs          JSON persistence
Srs/Leitner.cs          spaced repetition
Speech/Tts.cs           Azure TTS + on-disk cache
Ui/Screens.cs           message design, flowers, keyboards
Bot/Router.cs           update handling
Jobs/DailyNudge.cs      the evening reminder
Tools/SelfTest.cs       --check
```
