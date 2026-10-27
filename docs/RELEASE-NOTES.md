# 0.9.0 — first public release

A standalone Windows x64 music replacement editor with Skullgirls-inspired Art Deco styling.

- Modern Steam discovery and searchable audio library: music, story voices, announcers, UI sounds and character banks.
- Source/target record decks, spinning playback display, waveform source editing, intro/loop cuts, gain, fades and optional loop crossfade.
- WAV, MP3, FLAC, OGG/Opus, M4A/AAC, WMA and AIFF imports, including higher-depth/float WAV.
- Native ADPCM generation with independent output verification and converted playback.
- Editable projects, copied original-format sources, recovery drafts, native exports and optional Manager-compatible ZIP exports.
- Single-music-archive Apply/Restore with scoped backups, journals, staged verification and restart recovery.
- Self-contained .NET runtime and a minimal FFmpeg decoder with corresponding source/build information.

The user confirmed changed main-menu music and two clean loops; Restore returned the exact original archive hash. The downloaded release package has been confirmed working by the user. Compatibility limits are documented in COMPATIBILITY.md. The editor exposes 24 configured intro/loop music pairs; voice/SFX replacement and arbitrary singleton/menu editing are not included.

Download the win-x64 ZIP, extract everything to a writable folder outside the game, and run SkullgirlsAudioWorkshop.exe. Keep installation-backups, drafts, personal labels and projects when updating. Game assets and compatibility loaders are not included.

The same release provides matching Workshop source, FFmpeg source, its build script and SHA256SUMS.txt. See the GitHub Actions run for automated validation of the release commit.
