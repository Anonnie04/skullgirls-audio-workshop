using System.Security.Cryptography;
using System.Text.Json;
using SkullgirlsModManager;

if (args.Length == 3 && args[0] == "--export")
{
    string outputFolder = Path.GetFullPath(args[2]); Directory.CreateDirectory(outputFolder);
    string isolated = Path.Combine(outputFolder, "manager-" + Guid.NewGuid().ToString("N"));
    var imported = new Library(isolated).Import(Path.GetFullPath(args[1])); var entries = imported.Replacements();
    if (imported.Enabled || entries.Count != 1 || entries.Values.Single().Count != 2 || imported.LooseFiles().Count != 0) throw new Exception("Export package replacement scope or enabled state differs.");
    using var zip = System.IO.Compression.ZipFile.OpenRead(args[1]);
    foreach (var archive in entries) foreach (var asset in archive.Value)
    {
        using var stream = zip.GetEntry(archive.Key + "/" + asset.Key)!.Open(); using var saved = File.OpenRead(asset.Value);
        if (!SHA256.HashData(stream).SequenceEqual(SHA256.HashData(saved))) throw new Exception("Manager import changed exported audio.");
    }
    string packagePath = Path.GetFullPath(args[1]); using var packageStream = File.OpenRead(packagePath);
    var report = new { packagePath, packageSha256 = Convert.ToHexString(SHA256.HashData(packageStream)), isolatedImportPassed = true, importedDisabled = true, replacementCount = 2, exactImportedAudio = true, imported.Info.Name, imported.Info.Author, version = imported.Info.Version.ToString(), imported.Info.Description, gameFilesWritten = false };
    File.WriteAllText(Path.Combine(outputFolder, "export-manager-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(report)); return;
}
if (args.Length != 2) throw new ArgumentException("Usage: ManagerCheck GAME RESEARCH_OUTPUT");
string game = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
string libraryPath = Path.Combine(output, "isolated-manager-library");
if (Directory.Exists(libraryPath)) throw new IOException("Use a fresh isolated library.");
var mod = new Library(libraryPath).Import(Path.Combine(output, "Moonlit-Melee-Native-ADPCM-Test.zip"));
if (mod.Enabled) throw new Exception("Import unexpectedly enabled mod.");
var replacements = mod.Replacements();
if (replacements.Count != 1 || replacements["music-win.gfs"].Count != 2)
    throw new Exception("Unexpected replacement scope.");
string folder = ArchiveCompatibility.Installed(game) && Directory.Exists(Path.Combine(game, "data02")) ? "data02" : "data01";
string original = Path.Combine(game, folder, "music-win.gfs");
Archives.ValidateBuild(original, Path.Combine(output, "native-music-win.gfs"), replacements["music-win.gfs"]);
string built = Path.Combine(output, "manager-native-music-win.gfs");
if (File.Exists(built)) throw new IOException("Use a fresh manager build output.");
Archives.Build(original, built, replacements["music-win.gfs"]);
Archives.ValidateBuild(original, built, replacements["music-win.gfs"]);
string Digest(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
var result = new { isolatedImportPassed = true, importedDisabled = !mod.Enabled,
    archiveFolderFromManager = folder, replacementCount = 2,
    pythonBuildPassedManagerValidation = true, managerBuildPassedValidation = true,
    buildersByteIdentical = Digest(built) == Digest(Path.Combine(output, "native-music-win.gfs")),
    installedGameWritten = false };
File.WriteAllText(Path.Combine(output, "manager-validation.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
