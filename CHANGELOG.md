# Changelog

## 2.4.1 — 2026-09-26

- Added profile-owned companion-app preloading for Active Sky, SayIntentions, REX Core Atmos and other external tools.
- Added **Before Simulator**, **After Simulator Starts** and SimConnect-confirmed **Ready to Fly** launch stages with configurable launch delays and a bounded readiness fallback.
- Added direct executable selection, optional administrator launch, duplicate-process prevention and per-app **Leave Running** or **Close on Session End** cleanup.
- Companion apps are now stored with named flight profiles and restored when the active profile is reapplied at startup.
- Added a one-click switch to pause or resume CPU-spike and frame-stutter recording during simulator loading while keeping other dashboard telemetry active.
- Clarified the Custom Apps workflow, removed the unused command-line Arguments field and fixed its launch/cleanup dropdown controls.
- Expanded automated coverage to 59 passing tests.

## 2.4.0 — 2026-09-17

- Added flight-session history, performance trend graphs, session comparisons, and automatic simulator, GPU-driver and profile-change markers.
- Added read-only CPU/GPU balance recommendations for DLSS mode, render scaling and stable frame-rate targets, with safe `UserCfg.opt` backup support.
- Added VR runtime diagnostics and MSFS online-services health checks, including official MSFS 2024 service status, local networking, Microsoft/Xbox DNS and supporting services.
- Expanded saved profiles with import, export, duplicate, rename, simulator/aircraft/headset/monitor associations, and exact saved-versus-current differences.
- Added a one-click privacy-scrubbed support package for faster troubleshooting.
- Added service dependency and dependent visibility, pre-flight warnings, restart-command safety checks and a **Test Restart** action.
- Added a proper Windows installer and clean uninstaller.
- Added verified in-app installer downloads from official GitHub releases with SHA-256 validation.
- Kept the freeware release process simple and unsigned, without paid certificate requirements.
- Expanded automated coverage to 58 passing tests.

## 2.3.0

- Added the dedicated **Display / DLSS** tab, saved 2D/VR rendering modes, NVIDIA App model presets, active DLSS runtime version, driver details and an in-simulator DLSS information overlay switch.
- Preserved the Xbox, Gaming Services, authentication, game-save and networking stack after MSFS exits to protect live weather on subsequent launches.
- Improved protected-process handling and expanded automated validation to 50 tests.

## 2.2.1

- Added manual update checking, initial read-only display/DLSS inspection and improved dashboard FPS-source handling.
- Removed forced post-flight Xbox/Game Bar cleanup and clarified degraded telemetry states.

## 2.2.0

- Added opt-in online application/service guidance, local software identity inspection and safer unmatched-item guidance.
- Added selected-item sorting, clearer explanations, full hover text and profile persistence for online guidance.

## 2.1.0

- Added per-application **Restart** or **Left Closed** behavior, service restoration visibility and improved saved-profile state handling.
- Combined launch confirmations, improved post-flight shutdown and fixed application restart/restoration edge cases.
