using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsAudio;

internal static class MetadataChecks
{
    public static void Run(string game, string output)
    {
        Directory.CreateDirectory(output); var library = new GameLibrary(game);
        string Hash(string file) { using var f = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(f)); }
        var before = library.Sources.ToDictionary(s => s.Name, s => Hash(library.Archive(s.Name).FilePath));
        var english = library.Sources.Single(s => s.Name == "story_vo-win.gfs");
        var jp = library.Sources.Single(s => s.Name == "story_vo_jp-win.gfs");
        int enMatches = english.Tracks.Count(t => library.Segments(t).Single().Details!.Dialogue.Length > 0);
        int jpMatches = jp.Tracks.Count(t => library.Segments(t).Single().Details!.Dialogue.Length > 0);
        if (enMatches != 1473 || jpMatches != 1476) throw new Exception($"Dialogue matching changed: EN={enMatches}, JP={jpMatches}.");
        var track = english.Tracks.Single(t => t.Assets[0].EndsWith("/BeowulfEnd100_EN.wav")); var clip = library.Segments(track).Single();
        if (!clip.Details!.Dialogue.StartsWith("Hope you can rest in peace") || !track.Name.Contains("Hope you can rest")) throw new Exception("Dialogue is not attached to the correct readable clip.");
        var intro = english.Tracks.Single(t => t.Assets[0].EndsWith("/BeowulfIntro100_EN.wav")); var scene = library.Segments(intro).Single().Details!;
        if (!scene.Description.Contains("BeowulfIntro.ini") || !scene.Description.Contains("TV Announcer")) throw new Exception("Scene evidence is missing.");
        var split = english.Tracks.Single(t => t.Assets[0].EndsWith("/SquiglyIntro1800_1_EN.wav"));
        if (library.Segments(split).Single().Details!.Dialogue.Length > 0) throw new Exception("An unmatched split line was guessed.");
        var music = library.Tracks.Single(t => t.Mapped && t.Name == "Moonlit Melee");
        if (!library.Segments(music)[0].Title.EndsWith("Intro") || !library.Segments(music)[1].Title.EndsWith("Loop")) throw new Exception("Music segment roles missing.");
        var marie = library.Tracks.Single(t => t.Assets[0] == "ui/Win/UISound/JP_chars/charconfirm.marie.playable.p1.wav");
        if (ClipMetadata.EmbeddedTitle(library.Read(marie, library.Segments(marie)[0])) != "MARIE_characterselect1") throw new Exception("Embedded title not read.");
        var bank = library.Categories.Single(c => c.Title == "Announcers").Tracks[0]; var bankClip = library.Segments(bank)[0];
        if (!bankClip.Details!.Source.Contains("unidentified") || bankClip.Identity == null) throw new Exception("Unidentified bank clip mislabeled.");
        byte[] bytes = library.Read(track, clip); string key = ClipMetadata.Identity(track, clip, bytes);
        string labelsPath = Path.Combine(output, "labels-" + Guid.NewGuid().ToString("N") + ".json");
        var labels = new ClipLabels(labelsPath); labels.Save(key, new("A readable test label", "Personal description 日本語"));
        var reopened = new ClipLabels(labelsPath);
        if (reopened.Find(key)?.Description != "Personal description 日本語") throw new Exception("Label did not persist.");
        bytes[^1] ^= 1;
        if (reopened.Find(ClipMetadata.Identity(track, clip, bytes)) != null) throw new Exception("Changed clip inherited stale metadata.");
        reopened.Save(key, null); if (new ClipLabels(labelsPath).Find(key) != null) throw new Exception("Reset did not persist.");
        string corrupt = Path.Combine(output, "corrupt-labels-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(corrupt, "{broken");
        var bad = new ClipLabels(corrupt); if (bad.LoadWarning == null) throw new Exception("Corrupt label catalog was hidden.");
        try { bad.Save(key, new("Test", "")); throw new Exception("Corrupt catalog overwritten."); } catch (IOException) { }
        if (File.ReadAllText(corrupt) != "{broken") throw new Exception("Corrupt catalog changed.");
        foreach (var source in library.Sources) if (before[source.Name] != Hash(library.Archive(source.Name).FilePath)) throw new Exception("Installed archive changed.");
        File.WriteAllText(Path.Combine(output, "metadata-results.json"), JsonSerializer.Serialize(new { englishDialogueLinks = enMatches, japaneseDialogueLinks = jpMatches, sceneReferences = true, unmatchedSplitNotGuessed = true, musicRoles = true, embeddedTitle = true, unidentifiedBankExplicit = true, userLabelsPersist = true, changedContentDoesNotInheritLabel = true, resetPersists = true, corruptCatalogPreserved = true, installedArchivesUnchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {enMatches + jpMatches:N0} dialogue links, scene references, music roles, embedded title, saved labels/reopen/reset, stale-label rejection and unchanged game archives.");
    }
}
