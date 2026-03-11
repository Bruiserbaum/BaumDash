#define AppName      "BaumDash"
#define AppVersion   "2.5.17"
#define AppVersionFull "2.5.17-dev"
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

; Require .NET 8 Desktop Runtime
[Code]
function IsDotNet8Installed(): Boolean;
var
  Key: String;
begin
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Result := RegKeyExists(HKLM, Key) or RegKeyExists(HKCU, Key);
  if not Result then
  begin
    // Fallback: check for any 8.x version directory
    Result := DirExists(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.0')) or
              FileExists(ExpandConstant('{pf64}\dotnet\dotnet.exe'));
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8Installed() then
  begin
    if MsgBox(
      'BaumDash requires the .NET 8 Desktop Runtime.' + #13#10 +
      'It does not appear to be installed.' + #13#10#13#10 +
      'Download it from: https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10#13#10 +
      'Continue installing anyway?',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

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

    // Remove old stable AppId registry entries so Windows doesn't show a ghost uninstall entry
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
; Main application
Source: "{#PublishDir}\{#AppExeName}";                               DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
; Managed assembly + runtime manifests (required for framework-dependent launch)
Source: "{#PublishDir}\WinUIAudioMixer.dll";                         DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\WinUIAudioMixer.deps.json";                   DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\WinUIAudioMixer.runtimeconfig.json";          DestDir: "{app}"; Flags: ignoreversion
; Runtime assemblies
Source: "{#PublishDir}\Microsoft.Windows.SDK.NET.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\System.Security.Cryptography.ProtectedData.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\System.Speech.dll";                          DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\WinRT.Runtime.dll";                          DestDir: "{app}"; Flags: ignoreversion

; Config files — never overwrite if user already configured them
Source: "{#PublishDir}\ha-config.json";            DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\anythingllm-config.json";  DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\chatgpt-config.json";       DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\general-config.json";      DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\weather-config.json";     DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}";                   Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";         Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";             Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove runtime-created files on uninstall
Type: files; Name: "{app}\discord-debug.log"
Type: files; Name: "{app}\discord-token.txt"
Type: files; Name: "{app}\window-state.json"
