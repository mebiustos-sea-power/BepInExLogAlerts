# BepInEx Log Alerts

An Anchor Chain mod for Sea Power that displays BepInEx Warning / Error / Fatal messages at the top center of the game screen.

- Display name: BepInEx Log Alerts
- Mod ID: `mebiustos.bepinexlogalerts`
- DLL: `BepInExLogAlerts.dll`
- Version: 0.1.3
- Requirements: Sea Power, BepInEx 5.4.x, Anchor Chain

## How it works

The mod registers an `ILogListener` during initialization and collects startup messages. After the main menu is first displayed, it flushes the current BepInEx disk log, recovers earlier messages, and reconciles overlapping occurrence counts with the messages already received. There is no need to specify a loading order or modify other mods.

Startup completion uses the same `StartupScreen` checks as Harmony Startup Optimizer: an existing menu must be active with a `CurrentWindow`, and the loading screen must either be absent or have its rendering camera disabled. Recovery waits for the menu's first Repaint, then combines startup warnings and errors into one notification. This summary appears once per launch. Harmony Startup Optimizer itself is not a dependency.

New messages are received through `ILogListener`. The mod does not periodically poll or repeatedly read the log file to monitor new messages. Identical live messages with the same level, source, and body are grouped within a default 10-second window. At most two notifications are visible at once. Warnings disappear after 6 seconds, and errors/fatal messages after 10 seconds, measured from their first render. Repeated messages do not keep extending a notification's lifetime.

Popup notifications do not capture clicks or focus. Nothing is drawn when there are no visible notifications and the history is closed. The mod does not pause the game, and notification durations use real time rather than simulation time.

## History

Press `F8` to toggle the history. `Escape` or the Close button also closes it. Select a log to view its message and stack trace, or use Copy to copy its source, level, and message. Each row has the same severity stripe as its popup: yellow-orange for Warning and red for Error/Fatal. The selected row has a brighter background. Both panes support the mouse wheel and draggable scrollbars.

History remains available after notifications disappear. It retains up to 200 groups by default and reports how many older groups have been removed when the limit is reached. Recovered file entries are labeled "Startup" because the log file does not provide timestamps from which to recover their original times. The source is the name that emitted the log; it does not establish which mod caused the underlying problem.

## Muting individual notifications

Select an entry in the history and check "Do not notify me of this log again" to stop its popups. Uncheck it to resume notifications for that entry. Muted messages are still collected and retained in history, where they are marked "Muted".

Use "Reset all muted notifications" at the top of the history to clear every mute setting, including settings for messages absent from the current session's history. Mute settings persist across game restarts. BepInEx's own warnings and errors remain eligible by default.

A mute setting matches the combination of log level, source, and stored message body. It is independent of startup/live status, timestamp, and occurrence count. Changes to numbers or other text in the body make a different message. There are no wildcard or substring exclusions.

The same mute settings apply to startup summary counts and regular popups. No startup popup is shown if every startup message is muted. Muting a visible notification also removes it. Resuming notifications does not redisplay old popups or restore removed counts; it applies to future occurrences. History counts include muted occurrences, so they may differ from popup counts after notifications resume.

Settings are written to `BepInEx/config/mebiustos.bepinexlogalerts.mutedlogs` on the first change. Changes made in the history take effect only after saving succeeds. A failed save preserves the previous settings and reports the reason in the log and history. If the settings file is corrupt or unreadable, initialization reports the error without silently resetting or overwriting the file.

## Installation

1. Install Anchor Chain and its preloader.
2. Download and extract the ZIP from [Releases](https://github.com/mebiustos-sea-power/BepInExLogAlerts/releases/latest), then copy the `BepInExLogAlerts` folder into `Sea Power_Data/StreamingAssets`.
3. Enable Anchor Chain and BepInEx Log Alerts in the mod manager, then restart the game.

The GitHub ZIP contains the mod DLL, `_info.ini`, and this README. Building the repository does not publish an item to Steam Workshop.

## Configuration

The mod creates `BepInEx/config/mebiustos.bepinexlogalerts.cfg` on its first enabled launch. Configuration changes take effect on the next game launch.

| Section / key | Default | Meaning |
|---|---|---|
| Logging / IncludeUnityLogs | false | Also include Unity Log. These may be game messages whose originating mod is unknown. |
| Display / Width | 680 | Notification width at 1080p. |
| Display / Top | 20 | Distance from the top at 1080p. |
| Display / Scale | 1 | Additional scale multiplier after automatic resolution scaling. |
| Display / WarningSeconds | 6 | Warning duration in seconds. |
| Display / ErrorSeconds | 10 | Error/Fatal duration in seconds. |
| Display / RepeatSeconds | 10 | Time window for grouping identical messages. |
| Display / Language | Auto | Follow the game's language, or select English / Japanese. |
| Input / HistoryKey | F8 | Toggle the history. |
| Limits / HistoryEntries | 200 | Maximum retained history groups. |
| Limits / PendingEntries | 4096 | Maximum queued log events. |
| Limits / StartupLogMegabytes | 8 | Maximum MiB read from the startup log file. |

## Startup recovery and error handling

Recovering messages emitted before this mod initialized requires BepInEx `Logging.Disk.Enabled=true`, `AppendLog=false`, and disk logging of the desired levels. These are the default settings. Append mode has no reliable boundary between sessions, so file recovery is skipped with an explicit log and notification if `AppendLog=true`. Disabled or inaccessible disk logging and unsupported writer formats are also reported; directly received notifications continue.

Unity Log is excluded by default. Even when enabled in this mod, recovering earlier Unity messages requires BepInEx `Logging.Disk.WriteUnityLog=true`. Messages that were not written to disk before this mod initialized cannot be recovered.

If several log files exist, the mod obtains the path from the current process's actual disk writer. It does not select another process's log based only on the name `LogOutput.log`.

Large startup logs are read from the end up to the configured limit, with a notification if content was omitted. Each event retains up to 16,384 characters of its body and adds a truncation marker when necessary. Queue overflow is also reported with an omitted-event count. Consult the original log file for complete details.

The mod does not receive its own diagnostic logs. Listener callbacks isolate exceptions and avoid recursive logging; callback failures are reported in the log and history from the next update.

On-screen notifications are unavailable if this mod or Anchor Chain fails to load, or if the game never reaches a state in which the overlay can render.

## Building and verification

Use the Windows .NET 10 SDK. No NuGet package download is required. Create `Directory.Build.local.props` using `Directory.Build.local.props.example` and set the paths to the local game and Anchor Chain.

```powershell
.\build.ps1 -Test
```

Omit `-Test` to build and package only. Outputs are `dist/BepInExLogAlerts`, `dist/BepInExLogAlerts-v0.1.3.zip`, and `dist/SHA256SUMS.txt`. Only an explicit `-InstallLocal` installs files into StreamingAssets, after checking that the game is stopped.

Verification covers log parsing, startup/live overlap reconciliation, repeated messages and capacity limits, notification timing while rendering is stalled, and integration with actual BepInEx using Sea Power's bundled Mono runtime. Mono checks run in a separate process without launching the game; temporary logs are created and removed outside the game's log directory.

`StartupScreen.cs` uses the same readiness checks as Harmony Startup Optimizer. `ModActivation.cs` uses the same mod-enabled checks as Bulk Identification Requests.

`docs/agent-memory.md` is local working memory and is excluded from Git and distribution packages.

## Changes in 0.1.1

History selection, Close, Copy, and scrolling now use input read directly from the new Input System. IMGUI is used only for drawing, so history controls work when the game does not forward mouse events to OnGUI. Regression checks cover coordinate scaling at 1080p and 4K, display scaling, press/release behavior, the selected clipboard payload, and scrolling.

## Changes in 0.1.2

Added per-message mute/unmute and a reset-all control in the history. Settings are saved and applied consistently to startup summaries, regular popups, and notifications already visible. Resuming notifications does not replay past popups; muted events remain in history.

## Changes in 0.1.3

All history rows now show severity stripes using the same color definition as popups. Selection is indicated by the existing background highlight.

## Source and releases

- [Source code](https://github.com/mebiustos-sea-power/BepInExLogAlerts)
- [Releases](https://github.com/mebiustos-sea-power/BepInExLogAlerts/releases)

The Workshop icon is managed at `assets/workshop/workshop_icon.jpg`.
