# Skullgirls Audio Workshop

A Windows music replacement editor for Skullgirls, with an Art Deco record-player interface.
Browse game audio, prepare an intro and loop, hear the converted result, then export a mod or apply it with a verified backup.

**Version 0.9.0.** The downloaded Windows package has been confirmed working by the user. Local menu replacement and two loop boundaries have been confirmed in-game; broader game-build coverage remains ongoing.

## Download and run

1. Get **SkullgirlsAudioWorkshop-0.9.0-win-x64.zip** from [Releases](https://github.com/Anonnie04/skullgirls-audio-workshop/releases).
2. Extract the entire ZIP into a writable folder outside the game, such as Documents. Do not run it inside the ZIP.
3. Run **SkullgirlsAudioWorkshop.exe**.

The portable Windows x64 download includes the .NET runtime and audio decoder. No separate Mod Manager, FFmpeg installation or .NET installation is required. Windows 10/11 x64 is the intended platform; broader clean-PC testing remains ongoing. Windows supplies the ADPCM encoder, which must be available for generation.

You need your own supported PC game installation. Game audio, game archives and game-side compatibility loaders are not included. Automatic discovery opens the current Steam installation; **Open installation** selects another installation. Opening the library does not launch the game.

## Make a music replacement

1. Select a verified music target, then **Replace music…**.
2. On blue **Side A**, choose **Import audio…** and open your source.
3. Use **Edit source…** to set intro start, loop start and loop end. Adjust gain, fades or loop crossfade as needed.
4. Preview the edits. Choose **Generate & verify**, then **Play converted** on Side B.
5. Save an editable **.sgawproj** project. Source copying is on by default and preserves the original file format.
6. Export a native WAV pair or mod ZIP, or use **Apply / Restore…** with the game closed.
7. Test the replacement in-game. Use **Restore pre-apply music** to return to the verified state captured before Apply.

The editor currently exposes 24 configuration-backed intro/loop music pairs. Other discovered audio remains available for browsing and supported previews. The menu replacement used during development was a separate diagnostic; menu editing is not yet a verified library target.

## Import formats

WAV (PCM8/16, Microsoft ADPCM, 24-bit, float and supported extensible WAV), MP3, FLAC, OGG/Vorbis, OGG/Opus, M4A/AAC, WMA and AIFF.

Extended imports decode to 44.1 kHz stereo PCM16; multichannel inputs are downmixed. Native PCM8/16 and Microsoft ADPCM WAV keep their supported original mono/stereo rate for editing. Generated game audio uses the target's own sample rate and stereo MS ADPCM block layout, and is independently decoded for verification.

Limits: 128 MB input and 32 million decoded samples (about six minutes at stereo 44.1 kHz), with a ten-minute ceiling on the managed WAV path. Oversized sources are rejected rather than cut short. Import supports cancellation and a two-minute timeout. Protected, damaged and unsupported encodings produce an error. Output encoding is lossy; review the displayed clipping and timing adjustments.

## Backups and updates

Apply backs up the actual current archive, including any preexisting mods; it does not claim that the backup is an untouched Steam original. Unrelated entries are verified unchanged. Restore refuses unexplained external archive changes. Keep the game closed while preparing, applying, restoring or recovering.

Keep **installation-backups**, **drafts**, personal labels and your project/source folders when updating. Close Workshop before replacing its application files. Extract updates over the application folder while retaining these user-data folders, or keep the old folder until all its installations have been restored. Do not delete a folder containing needed restore data.

## Documentation

- [User guide](docs/USER-GUIDE.md)
- [Compatibility and known limits](docs/COMPATIBILITY.md)
- [Build and corresponding source](docs/BUILD.md)
- [Validation record](docs/VALIDATION.md)
- [0.9.0 release notes](docs/RELEASE-NOTES.md)
- [Proposal and remaining work](STATUS.md)

## License and credits

Workshop is GPL-3.0; see [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md). Playback, managed WAV decoding and game discovery adapt code from [Skullgirls Mod Manager](https://github.com/Anonnie04/skullgirls-mod-manager).

The release decoder is a separate, replaceable FFmpeg executable built from unmodified pinned source without optional third-party codec libraries or GPL-only components. Its LGPL texts and build information are included; corresponding FFmpeg source is attached to the same release. The Windows package also carries .NET's third-party notices.

This is an independent community tool. Skullgirls and its game assets belong to their respective owners.
