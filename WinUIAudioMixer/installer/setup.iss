#define AppName      "BaumDash"
#define AppVersion   "2.8.2"
#define AppVersionFull "2.8.2"
#define AppPublisher "Bnuss"
#define AppExeName   "WinUIAudioMixer.exe"
#define PublishDir   "..\WinUIAudioMixer\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"

[Setup]
AppId={{F1E2D3C4-B5A6-4789-9ABC-DEF012345678}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
OutputDir=output
OutputBaseFilename=BaumDash-Setup-{#AppVersionFull}
SetupIconFile=..\WinUIAudioMixer\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
CloseApplications=yes
MinVersion=10.0.22621
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Self-contained — .NET runtime is bundled in the exe, no separate check needed.

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  OldPath: String;
  OldRegKey: String;
begin
  if CurStep = ssInstall then
  begin
    // Remove old Program Files installation left by earlier admin-privilege installs
    OldPath := ExpandConstant('{pf}\BaumDash');
    if DirExists(OldPath) then
      DelTree(OldPath, True, True, True);

    OldPath := ExpandConstant('{pf64}\BaumDash');
    if DirExists(OldPath) then
      DelTree(OldPath, True, True, True);

    // Remove old stable AppId registry entries
    OldRegKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A3F2E1D0-7B4C-4A8E-9F1D-2C5B6E3A8F90}_is1';
    RegDeleteKeyIncludingSubkeys(HKLM, OldRegKey);
    RegDeleteKeyIncludingSubkeys(HKCU, OldRegKey);
  end;
end;

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; All published files (self-contained — runtime bundled in exe)
Source: "{#PublishDir}\{#AppExeName}";                DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "{#PublishDir}\*.dll";                        DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\runtimes\*";                   DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

; Config files — never overwrite if user already configured them
Source: "{#PublishDir}\ha-config.json";           DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\anythingllm-config.json";  DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\chatgpt-config.json";      DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\general-config.json";      DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\weather-config.json";      DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}";                   Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";         Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";             Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall

[UninstallDelete]
Type: files; Name: "{app}\discord-debug.log"
Type: files; Name: "{app}\discord-token.txt"
Type: files; Name: "{app}\window-state.json"
