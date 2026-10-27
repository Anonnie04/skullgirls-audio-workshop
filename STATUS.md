# Proposal status — 27 October 2026

Version 0.9.0 implements the first music-editing workflow: discovery and browsing, source cuts and processing, common-format import, native generation and converted preview, projects/recovery drafts, native/mod ZIP exports and app-owned single-archive Apply/Restore.

Additional implemented features include voice/announcer browsing, localization-linked descriptions, personal labels and Art Deco record decks. Voice/SFX bank replacement remains a later milestone.

## Release position

Version 0.10.0 is the current release; version 0.9.0 was the first stable public release. The user downloaded the published Windows package and confirmed it works. Packaging supplies the .NET runtime and a minimal FFmpeg decoder built from pinned source, with matching source/build information. The GitHub Actions workflow validates codec imports, conversion, project/source reopening, exports, installation/recovery and application startup using synthetic fixtures without a game installation.

Local evidence includes all 24 configured target profiles, independent decoding, real music-archive preservation, 22 installation/recovery scenarios and user-confirmed menu replacement with two clean loops. Restore returned the exact original archive hash. See docs/VALIDATION.md and the workflow run for results at the release commit.

## Still open

Version 0.10.0 adds multi-song mod projects (.sgawmod), combined folder/ZIP exports and one-archive batch Apply/Restore, plus explicit crop-end controls and per-song full-source playback. Collection, legacy compatibility, export, crop/bypass rendering and isolated Apply/Restore/recovery checks pass. Local listening confirmed cropped full-song looping and separate-intro playback/looping. A 21-song installation preserved all 58 unrelated assets. One initial-start static report remains unexplained, with no added burst found in the compared source/converted opening; broader acceptance remains open. Source drag and drop and draggable waveform markers are also included. See [TODO.md](TODO.md).

- Full clean-user-PC interactive acceptance and Windows codec availability beyond tested systems.
- Explicit intro-transition and post-Restore audible checks; broader stage, alternate-music and game-build coverage.
- Loader requirements without the locally recognized setup; custom loaders are unverified.
- Broader OS/filesystem/power-loss acceptance. Generic multi-archive installation is unsupported and is not claimed.
- Expansion beyond the 24 configured music pairs, including product support for the diagnostic menu pair.
- Voice/SFX bank replacement and structural/event mappings as a separate future milestone.

The original proposal remains in PROPOSAL.md. Historical development detail is in docs/DEVELOPMENT-HISTORY.md and Research/FINDINGS.md; private test reports and game-derived fixtures are not distributed.
