@echo off
cd /d "%~dp0"
if exist "dist\SkullgirlsAudioWorkshop\SkullgirlsAudioWorkshop.exe" (
  start "" "dist\SkullgirlsAudioWorkshop\SkullgirlsAudioWorkshop.exe" %*
  exit /b
)
if exist "App\bin\Release\net9.0-windows\SkullgirlsAudioWorkshop.exe" (
  start "" "App\bin\Release\net9.0-windows\SkullgirlsAudioWorkshop.exe" %*
  exit /b
)
echo Run Build.ps1 first to build the application.
pause
