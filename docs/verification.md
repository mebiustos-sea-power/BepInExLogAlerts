# Verification — 2026-11-02

Command: `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1 -Test`

- Release build: succeeded, 0 warnings, 0 errors.
- 11 behavior checks passed: multiline BepInEx parsing; combined severity flags; source filtering; startup overlap/repeated occurrence counts; separate post-startup alerts; queue overflow reporting; snapshot failure preservation; concurrent capture across startup/live phases; repaint-based notification duration; bounded persistent history; repeat grouping and maximum screen occupancy.
- Sea Power bundled Mono integration: passed with the installed BepInEx 5.4.23.2. Checked actual buffered disk writer/flush and live subscription, alternate log filename discovery, callback exception isolation, and real game menu/loading/language contracts without creating singletons.
- StartupScreen matches Harmony Startup Optimizer except namespace.
- Distribution DLL matches the built DLL. Package contains exactly BepInExLogAlerts.dll, _info.ini, README.md; local agent memory excluded.

Game launch, installation, actual screen/input validation, and Workshop publication were not performed.

## 0.1.1 — history interaction regression fix

- Same build/test command passed with 0 warnings/errors.
- 19 behavior checks passed. Eight new checks exercise pointer-only row selection at 1080p/4K/scaled screens, copying the selected diagnostic payload, Close/reopen, release-outside/focus cancellation, independent pane scrolling, scrollbar dragging and scrolled row hits, inactive history, and incoming logs between press/release. These tests do not require IMGUI input events.
- The existing four real BepInEx/game Mono integration checks passed.
- Corrected package deployed at explicit user request after shared launcher confirmed the game stopped. DLL/README/metadata match dist.
- Live gameplay clicking, actual OS clipboard operation, and actual drawing of the corrected UI were not exercised by this chat.

## 0.1.2 — individual notification suppression and reset

- Command: `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1 -Test`. All Release builds passed with 0 warnings/errors.
- 30 behavior checks passed (19 existing + 11 suppression regressions): exact level/source/body matching, startup/live equivalence, retained muted history, visible-card removal, future-only resume counts, startup-summary filtering and no resurrection, persistent selective/bulk reset, failed-save state preservation, corrupt-file rejection without overwrite, checkbox clicks at multiple scales, reset with absent history, canceled/evicted selections, and queued occurrences received before a reset.
- The queued-occurrence regression was first observed failing, then passed after receipt-time eligibility was retained.
- Five game-bundled Mono integration checks passed, including actual atomic rule-file replacement, locked-file save failure preserving prior data/in-memory state, temporary-file cleanup and successful reset after unlocking.
- Package contains only DLL/README/_info.ini and matches build outputs. StartupScreen still matches the requested optimizer logic. DLL SHA256: 486bb44ad402d1a6998ae1dcc51eba737b38f667606645622eb134f0e2fad366.
- Game-side installation was not changed. Actual checkbox rendering/clipboard/game UI and fresh game startup with saved suppression rules were not tested in a live game session.


## 0.1.3 — first public release

- The color-change build passed with 0 warnings/errors: 30 behavior checks and 5 integration checks using the game's bundled Mono runtime. The runtime DLL is unchanged by release preparation.
- History now uses the same yellow-orange Warning / red Error/Fatal stripes as popup notifications, including unselected rows. Selected rows retain background highlighting.
- Both mod-manager descriptions now contain only the one-sentence functional summary.
- The release ZIP contains exactly BepInExLogAlerts/BepInExLogAlerts.dll, BepInExLogAlerts/_info.ini and BepInExLogAlerts/README.md. SHA256SUMS.txt covers the ZIP. Local configuration, local agent memory, game assemblies and build intermediates are excluded.
- Previous history pointer operation was confirmed working by the user. Rendering of the 0.1.3 color change has not been checked in a live game session by this chat.
