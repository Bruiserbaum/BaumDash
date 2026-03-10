#define AppName      "BaumDash"
#define AppVersion   "2.3.5"
#define AppPublisher "Bnuss"
#define AppExeName   "WinUIAudioMixer.exe"
#define PublishDir   "..\WinUIAudioMixer\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"

[Setup]
AppId={{A3F2E1D0-7B4C-4A8E-9F1D-2C5B6E3A8F90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=output
OutputBaseFilename=BaumDash-Setup-{#AppVersion}
SetupIconFile=..\WinUIAudioMixer\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Main application (single-file publish — all managed code bundled into the exe)
Source: "{#PublishDir}\{#AppExeName}";                    DestDir: "{app}"; Flags: ignoreversion
; Native WPF/DirectX helpers required at runtime (not bundled into single-file)
Source: "{#PublishDir}\D3DCompiler_47_cor3.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PenImc_cor3.dll";                  DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PresentationNative_cor3.dll";      DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\vcruntime140_cor3.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\wpfgfx_cor3.dll";                  DestDir: "{app}"; Flags: ignoreversion

; Config files — never overwrite if user already configured them
Source: "{#PublishDir}\discord-client-id.txt"; DestDir: "{app}"; Flags: onlyifdoesntexist
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
