// Adapted from Skullgirls Mod Manager App/GameLocation.cs (GPL-3.0).
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace SkullgirlsAudioWorkshop;

internal static class GameLocation
{
    public static string Find()
    {
        string? configured = Environment.GetEnvironmentVariable("SKULLGIRLS_GAME_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "SkullGirls.exe"))) return configured;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")
        };
        try
        {
            if (OperatingSystem.IsWindows() && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam) roots.Add(steam);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        foreach (string steam in roots.ToArray())
        {
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(libraries))
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"")) roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return roots.Select(root => Path.Combine(root, "steamapps", "common", "Skullgirls"))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "SkullGirls.exe"))) ?? "";
    }
}
