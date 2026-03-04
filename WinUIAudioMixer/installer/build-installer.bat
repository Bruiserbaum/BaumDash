@echo off
setlocal

set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set PROJECT=..\WinUIAudioMixer\WinUIAudioMixer.csproj

echo [1/2] Publishing Release build...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false --no-restore -o "..\WinUIAudioMixer\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)

echo.
echo [2/2] Building installer...
%ISCC% setup.iss
if errorlevel 1 (
    echo ERROR: Inno Setup compile failed.
    pause
    exit /b 1
)

echo.
echo Done! Installer is in installer\output\
start "" "output"
