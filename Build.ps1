param([switch]$Portable, [string]$OutputDirectory = 'dist/SkullgirlsAudioWorkshop')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
if (-not (Test-Path 'vendor/ffmpeg/ffmpeg.exe')) { throw 'Build or download the release decoder first; see vendor/ffmpeg/README.md.' }
if ($Portable) {
    dotnet publish App/App.csproj -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$PSScriptRoot/.build/bin/" -o $OutputDirectory
} else {
    dotnet publish App/App.csproj -c Release --self-contained false -p:NuGetAudit=false "-p:BaseOutputPath=$PSScriptRoot/.build/bin/" -o $OutputDirectory
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $OutputDirectory 'README.md')
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'docs') -Force | Out-Null
Copy-Item -Path 'docs/*.md' -Destination (Join-Path $OutputDirectory 'docs')
Write-Host "Ready: $OutputDirectory/SkullgirlsAudioWorkshop.exe"
