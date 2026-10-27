# Multi-song mod projects (0.10.0)

Open **Mod projects…** from the library to create or open a **.sgawmod** collection. Choose a target and Add song, or import an existing single-song **.sgawproj**. In the music editor, choose **Use in mod project** to return that song's source and settings to the collection. Edit and remove songs independently. Targets sharing an asset cannot both be added.

To import a source, drag one supported audio file from Explorer onto **Side A** (including its disc/buttons) or the **waveform**, or choose **Import audio…**. Both routes validate and decode through the same importer. A successful import replaces the source and resets cuts/processing; a failed import retains the current source and settings. Folders, multiple files, unsupported types and drops during another operation are not accepted. Dropping does not move or modify the original file.

The collection has shared name, author, version and description. Save mod preserves every song's source, target fingerprints, cuts, gain, fades and crossfade. Original-format source copies are on by default; keep the adjacent **.sgawmod.sources** folder with the mod project. Missing/changed sources are reviewed through Edit song. Recovery drafts are stored under **mod-drafts**, and **Recover mod draft…** reopens them. Closing without saving keeps a recovery draft.

Choose **Generate all**, then select songs and **Play converted** to check the actual generated intro and loop. Editing a song invalidates the batch; metadata changes retain verified audio. Saving copies the sources and retains valid generated output. Reopening always requires fresh generation.

**Export mod folder…** creates a new folder with mod.json, export.json and all native replacement paths. **Export mod ZIP…** creates one Manager-compatible ZIP with the same contents. Sources and private local paths are excluded from exports. Existing exports are preserved.

With the game closed, **Apply / Restore…** prepares all songs together. One complete music archive is rebuilt and verified, unrelated entries are preserved, and one scoped backup/journal supports Restore and interrupted-operation recovery. Restore/recover the previous transaction before another Apply. Only one music archive and up to 24 non-conflicting pairs are supported.

## Crop and full-song mode

In **Edit source…**, place the waveform cursor and choose **Crop end at cursor [E]**, or enter **Crop end (s)**. **Use full ending** restores the source's full duration. Audition the repeating seam, then generate and listen to the converted result.

You can also drag the **INTRO**, **LOOP** (intro-to-loop split), and **CROP END** vertical lines directly in either waveform. Grab the line or its small handle; the resize cursor and gold highlight identify the active marker. Time fields update as you drag, with zoom/scroll respected. Markers stop before crossing their neighbors. Press Escape during a drag to cancel that movement. Click away from a marker to keep using waveform seeking and boundary-placement modes. In full-song mode, the start stays fixed at zero and only the end marker is draggable.

**Start full song (skip intro)** ignores the saved intro markers and loops source 0 through the crop end. Old markers are retained for use if the option is unchecked. The game still requires an intro file, so generation creates one silent native codec block (about 3 ms) followed by the full-source loop. Fade-in applies at the start of that loop. This output passes independent decoding and isolated installation checks. Local listening confirmed full-song looping and a separate-intro track's transition. One initial-start static report remains unexplained; no added startup burst appeared in the compared source/converted opening. Broader stage and system listening coverage remains ongoing.

Existing .sgawproj files remain supported; an absent SkipIntro setting defaults to false.
