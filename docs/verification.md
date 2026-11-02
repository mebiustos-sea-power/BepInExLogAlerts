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


## GitHub documentation language correction

- Translated the repository README, versioned release notes, and GitHub download ZIP's README into English. This correction changes documentation only; no new runtime version is introduced.
- Repacked the existing v0.1.3 ZIP by replacing only README.md, then regenerated SHA256SUMS.txt. Verified the three archive entries, the English README, and unchanged DLL and _info.ini bytes against the previous archive.
- Existing runtime tests were not repeated for this documentation-only correction. Workshop files, the local three-file package used for Workshop, and the game installation were not changed. The published v0.1.3 source tag remains unchanged.


## 0.1.4 — independent popup notification levels (2026-11-03)

- Command: `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1 -Test`. All Release builds passed with 0 warnings/errors.
- All 42 behavioral checks passed. The original 30 regressions remain covered. Added coverage includes level filtering with retained history, existing-card removal, future-only resume and queued muted events, independent per-message settings, persistence, failed-save rollback, invalid settings, three positive checkbox controls and reset with empty history at multiple scales, all eight level combinations for live popups/startup summaries, and independent Error/Fatal removal/resume.
- All five game-bundled Mono integration checks passed. Persistence checks cover released version-1 individual settings, all three independent level choices, atomic replacement, locked-file failure preserving the entire policy, and reset to all levels enabled with individual rules cleared.
- Default checkbox state is enabled for Warning, Error and Fatal. The same shared policy applies to live popups and startup-summary contributions. Disabled levels remain in history. Reset enables every level and clears individual rules in one saved snapshot.
- Backward reading is limited to the individual-setting format used by released v0.1.3. Its IDs are preserved and all levels default to enabled. The unreleased Warning-only development format has no migration path and is rejected explicitly without overwriting it.
- Package/ZIP contain exactly DLL, _info.ini and the English README. Archive CRC, source/build byte equality and SHA256SUMS.txt were verified. DLL SHA256: 1fdc6f7f0085bdabd435b2dc77a4c9b6d849a5f6dca05850d2bba1087d20d7a0. ZIP SHA256: d59057a78e14c495b4520a89dc4bde2d0dde21ea88cf5abca75223f98b148d85.
- No game launch or live visual validation was performed. The verified three-file package was subsequently deployed locally at the user's request. GitHub publication uses the same tested DLL and ZIP.


## 0.1.5 — preserve popup levels when clearing individual mutes (2026-11-03)

- Added a regression covering the bulk-unmute button for all eight Warning/Error/Fatal combinations, including future popup eligibility, retained history, and the inactive button with no individual rules. Before the fix, it failed with "Bulk unmute changed the selected popup levels."
- Updated existing persistence, failed-save, and multi-scale UI checks to the restored individual-only bulk-unmute contract. Queued level-filtered events are now tested with an explicit level re-enable rather than an unrelated bulk reset.
- Command: `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1 -Test`. All Release builds passed with 0 warnings/errors; all 43 behavior checks and all five game-bundled Mono integration checks passed.
- Disk persistence checks cover the released version-1 settings upgrade, current version-2 settings, mixed enabled/disabled levels after bulk unmute and reload, atomic replacement, and locked-file failure preserving previous settings.
- Product changes are limited to individual-only rule counting, clearing individual rules while retaining the immutable level snapshot, and the version bump. Button count, enabled state, and pointer hit testing share that count. NotificationRuleStore, settings paths/keys/formats, existing UI text, logging, startup recovery, and game integration are unchanged. No code comments were changed.
- ZIP CRC and the exact three runtime files were verified against the build, README, and metadata; SHA256SUMS.txt matches the ZIP. DLL SHA256: 9ece3dd71d3d0f713ebb014db810f44094cbcb050a5904cc0920b01da5dd9f53. ZIP SHA256: 85547c1cfd4db965fdc8e90e201af7343cb08bfbbe33b1d19503a541eda768f2. The existing v0.1.4 ZIP is unchanged.
- No live game launch, visual validation, game deployment, local preference edits, or Steam Workshop update was performed during this patch.
