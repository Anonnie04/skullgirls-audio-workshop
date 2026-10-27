param([Parameter(Mandatory=$true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$sourceChanges = @(git status --porcelain)
if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -gt 0) { throw 'Commit intended source changes before packaging; the application source ZIP must match the release commit.' }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$version = ([xml](Get-Content App/App.csproj -Raw)).Project.PropertyGroup.Version
foreach ($required in @('SkullgirlsAudioWorkshop.exe','coreclr.dll','System.Windows.Forms.dll','LICENSE','THIRD-PARTY-NOTICES.md','tools/ffmpeg/ffmpeg.exe','tools/ffmpeg/SOURCE.txt','tools/ffmpeg/COPYING.LGPLv2.1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Portable package component missing: $required" }
}
if ((Get-Content (Join-Path $publish 'tools/ffmpeg/SOURCE.txt') -Raw) -notmatch 'Unmodified FFmpeg 8.1.3') { throw 'Use the corresponding-source release decoder, not a development binary.' }
$files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($publish,$file.FullName).Replace('\','/')
    if ($relative -match '(^|/)(installation-backups|drafts|mod-drafts|generated|labels|Mods|Data)(/|$)' -or $relative -match '\.(gfs|wav|sgawproj|sgawmod|pdb)$') { throw "Private or unexpected content in publish directory: $relative" }
}
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
$binary = Join-Path $PSScriptRoot "artifacts/SkullgirlsAudioWorkshop-$version-win-x64.zip"
if (Test-Path -LiteralPath $binary) { throw 'Package already exists; choose a fresh artifacts directory or version.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish,$binary,[IO.Compression.CompressionLevel]::Optimal,$false)
git archive --format=zip --output="artifacts/SkullgirlsAudioWorkshop-$version-source.zip" HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source packaging failed.' }
foreach ($required in @('ffmpeg-8.1.3-source.tar.xz','build-ffmpeg.sh')) { if (-not (Test-Path "artifacts/$required")) { throw "Corresponding source missing: $required" } }
$digests = foreach ($file in Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.zip','.xz','.sh' }) { "{0}  {1}" -f (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$file.Name }
$digests | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
Write-Host 'Portable Windows package, matching application source and decoder source are ready.'
