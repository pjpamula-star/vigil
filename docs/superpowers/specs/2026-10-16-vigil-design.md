# Vigil: design

Vigil is a Windows 11 tray app for Windows laptops. It keeps the PC awake while Claude Code is working, lets it sleep once the work is done, protects the hardware, and tells Patrick what happened while he was away. Research is in `docs/research/2026-10-16-research.md` and mockups are in `docs/mockups/`.

## Why

You leave Claude Code running in the terminal and walks away. Windows Modern Standby suspends the terminal minutes later, so the work stops. Vigil should:

- the PC to stay awake **only while Claude works**, never idle forever
- to know at a glance that something is running
- to see, when he comes back, a short summary of what changed
- to follow along and reply from his phone

## Goals

1. **Auto keep-awake** driven by Claude Code hooks. It works with the lid open or closed, plugged in or on battery.
2. **Safe by construction.** Original power settings are journaled before any change and restored afterwards: on stop, on crash (next start), and by a watchdog scheduled task.
3. **Hardware protection.** Heat (Windows thermal-throttle counters) and low battery first ease off by capping the CPU, then hibernate.
4. **Short summaries.** Each session gets:
   - a headline of 20 words or fewer, written by Claude Haiku
   - exact fact chips read from the transcript
   - a timeline of meaningful steps

   These are never dropped: questions to the user, errors and failed tests, installs, deletions, commits, pushes and deploys, and usage limits.
5. **Welcome back.** After an unlock, a recap appears if anything happened while you were away.
6. **Vigil window.** Every session, summarized, with the live claude.ai Remote Control page embedded so he can read and reply without the terminal.
7. **Phone.** One-click setup of Claude Code Remote Control (`remoteControlAtStartup`). Pushes are suppressed while he's at the PC (`CLAUDE_CLIENT_PRESENCE_FILE`). Optional short "phone notes" via ntfy.

## Non-goals and anti-goals

- **No timer as a main mode.** "Keep awake for…" lives only in the right-click menu, and every manual hold expires on its own. There's no "Always" mode.
- **Never leave the PC awake by accident.** If Vigil crashes, the next start or the watchdog restores the settings.
- **No admin rights and no kernel drivers.**
- **Vigil never controls Claude.** It doesn't kill, approve or interrupt anything. When it's hot it hibernates the whole PC, and Claude resumes after.
- **No custom remote server.** Phone access uses Anthropic's official Remote Control.

## How it works

### Hook bridge
Vigil installs **command hooks** into `~/.claude/settings.json`, with a backup and a clean uninstall:

```
cmd.exe /d /c curl.exe -s -m 2 --data-binary @- -H "Content-Type: application/json" http://127.0.0.1:47823/hook >nul 2>&1 & exit /b 0
```

- The hooks are `async: true`, so they never slow Claude down.
- They always exit 0, so there are no "hook error" notices when Vigil isn't running.
- Events used: SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, PostToolUseFailure, PermissionRequest, Notification, Stop, StopFailure, SubagentStart, SubagentStop, SessionEnd.
- Vigil listens on 127.0.0.1:47823 only.

### Session states

| State | Entered on |
|---|---|
| **Working** | prompt, tool use, subagent activity, or Stop with background tasks |
| **Needs you** | PermissionRequest, or Notification of type permission_prompt, elicitation or agent_needs_input |
| **Paused** | StopFailure with rate_limit (a usage-limit wait) |
| **Done** | Stop |
| **Failed** | any other StopFailure |
| **Ended** | SessionEnd |

A Working session with no events for 45 minutes is treated as quiet.

### Keep-awake policy

**Hold** while any of these is true:

- a session is Working
- a session has been Needs you for less than 60 minutes (setting)
- a session has been Paused for a usage limit for less than 5 hours (setting)
- a manual hold hasn't expired

**Release** 2 minutes after the last reason ends.

**While holding:**
- `PowerSetRequest` with SystemRequired and ExecutionRequired
- `powercfg` overrides on the active scheme: STANDBYIDLE AC/DC = 0 and LIDACTION AC/DC = 0. The original values are journaled first in `%LOCALAPPDATA%\Vigil\journal.json`.
- When the lid closes while holding, Vigil locks the PC (setting, on by default).

### Safety

Poll every 10 seconds while holding.

- **Warm.** Triggers when the min `% Passive Limit` is below 100, or `Throttle Reasons` is nonzero, for 30 seconds. On battery, it also triggers at 20% or below. Vigil caps the CPU at 60% (PROCTHROTTLEMAX, journaled) and notifies.
- **Hot.** Triggers when it's still throttling 3 minutes after easing off, or the passive limit is 50 or below for 30 seconds. On battery, it also triggers at 8% or below. Vigil restores all settings, logs the event for Welcome Back, and hibernates (falling back to sleep).
- **Recovery.** After 5 calm minutes, the CPU cap is removed.

### Summaries

- At Stop, Vigil reads the transcript slice for that turn.
- Facts are extracted deterministically:
  - edited files
  - commands
  - test pass and fail counts
  - commits, pushes, installs and deletes
  - tool errors
  - questions asked
- The headline comes from this call, run in an isolated temp folder:

  ```
  claude -p --model haiku --setting-sources project --no-session-persistence --strict-mcp-config --tools "" --system-prompt …
  ```

  Measured at about 3 seconds on this PC, and it doesn't trigger hooks.
- If that call fails, the headline falls back to the first sentence of Claude's last message.
- The "Now" line (what Claude is doing right now) is derived from the latest PreToolUse event without an LLM.

### Away and Welcome back

- The lock time is recorded.
- On unlock, if you were away for 3 minutes or more and any session changed state, the Welcome back window opens.
- The presence file exists while unlocked and is removed on lock.

### UI

Apple-style dark glass, one color per meaning:

- blue = working
- orange = needs you
- green = done
- red = danger
- lavender = brand

**Surfaces:**
- **Tray icon:** the Night-watch eye in idle, working, needs-you, hot and manual variants.
- **Popover** (left-click): a headline sentence, session rows, "Open Vigil", and hold-to-stop.
- **Right-click menu:** keep awake for 1h, 2h or until 8 AM; Welcome back; Settings; Quit.
- **Vigil window:** a session list and detail (headline, chips, timeline), plus a Conversation tab that embeds `claude.ai/code/session_…` in WebView2. The URL is read from the transcript, with an "Open in browser" fallback.
- **Welcome back window.**
- **Settings.**

### Stack (builder's call)

- C# on .NET 10 with WPF, using WinForms only for NotifyIcon, and WebView2.
- Published as a self-contained single-file `Vigil.exe`.
- Data lives in `%LOCALAPPDATA%\Vigil`.
- Starts with Windows via the HKCU Run key.

## Left to the builder

- Exact copy text, spacing and animations.
- The parsing heuristics for test output.
- Settings layout.
