using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsAudio;

internal static class OrganizationChecks
{
    public static void Run(string game, string output)
    {
        Directory.CreateDirectory(output); var library = new GameLibrary(game);
        if (library.Sources.Count != 6 || library.Tracks.Count != 3258) throw new Exception("Organization duplicated or removed inventory items.");
        var category = library.Categories.Single(c => c.Title == "Announcers");
        string[] expected = ["characters/hitspark.snd-wav", "characters/hitspark_alt1.snd-wav", "characters/hitspark_alt2.snd-wav", "characters/hitspark_alt3.snd-wav", "characters/hitspark.snd-wav_jpvoice", "characters/hitspark_alt1.snd-wav_jpvoice", "characters/hitspark_alt2.snd-wav_jpvoice"];
        if (!category.Tracks.Select(t => t.Assets[0]).SequenceEqual(expected)) throw new Exception("Announcer banks are missing or misordered.");
        if (!category.Tracks.All(t => library.Tracks.Contains(t) && t.Name.Contains("Announcers"))) throw new Exception("Announcer category is disconnected from main library search.");
        var english = library.Sources.Single(s => s.Name == "story_vo-win.gfs").Tracks;
        int line100 = Array.FindIndex(english, t => t.Assets[0].EndsWith("/BeowulfEnd100_EN.wav"));
        int line1000 = Array.FindIndex(english, t => t.Assets[0].EndsWith("/BeowulfEnd1000_EN.wav"));
        if (line100 < 0 || line1000 <= line100) throw new Exception("Story clip numbers are sorted lexically rather than numerically.");
        var ui = library.Tracks.Single(t => t.Assets[0] == "ui/Win/UISound/charconfirm.bigband.p1.wav");
        var jpUi = library.Tracks.Single(t => t.Assets[0] == "ui/Win/UISound/JP_chars/charconfirm.bigband.p1.wav");
        if (!ui.Name.Contains("Big Band") || !ui.Name.Contains("Player 1") || ui.Name == jpUi.Name) throw new Exception("Character/player/language labels were lost.");
        var results = new List<object>();
        foreach (var track in category.Tracks)
        {
            string file = library.Archive(track.Archive).FilePath;
            string Hash() { using var f = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(f)); }
            string before = Hash(); var clips = library.Segments(track);
            if (clips.Length == 0) throw new Exception("Announcer bank has no readable clips.");
            foreach (var clip in new[] { clips.First(), clips.Last() }) ManagedDecoder.Decode(library.Read(track, clip));
            if (Hash() != before) throw new Exception("Installed archive changed.");
            results.Add(new { track.Name, track.Archive, asset = track.Assets[0], clips = clips.Length, sampleDecodes = 2 });
        }
        File.WriteAllText(Path.Combine(output, "organization-results.json"), JsonSerializer.Serialize(new { physicalArchives = library.Sources.Count, libraryItems = library.Tracks.Count, categories = library.Categories.Select(c => c.Title), announcers = results, naturalStoryOrder = true, readableUiLabels = true, installedArchivesUnchanged = true }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: all seven announcer banks exposed and previewable; no duplicated inventory; natural story order; readable character/player/language labels; installed archives unchanged.");
    }
}
