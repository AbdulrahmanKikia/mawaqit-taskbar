; Inno Setup 6 script — builds MawaqitWidget-Setup.exe
; Compile with:  ISCC.exe installer\mawaqit-widget.iss
; Or:            .\scripts\build-installer.ps1
;
; Expects release binary at dist\widget.exe (from build-release.ps1).

#define MyAppName "Mawaqit Widget"
#define MyAppVersion "0.1.2"
#define MyAppPublisher "Mawaqit Widget"
#define MyAppURL "https://github.com/AbdulrahmanKikia/mawaqit-taskbar"
#define MyAppExeName "Mawaqit Widget.exe"

[Setup]
AppId={{A7C3E91F-2B4D-4F18-9E6A-8C1D0B7F4A22}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install — no Administrator prompt
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=MawaqitWidget-Setup-{#MyAppVersion}
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=welcome.txt
InfoAfterFile=after-install.txt
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start with Windows"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "..\dist\widget.exe"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion
Source: "..\assets\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

; Launch is optional — auto-launch shows a scary error when Smart App Control
; / Application Control blocks the unsigned exe (CreateProcess 4551).
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent unchecked; WorkingDir: "{app}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Stop a running instance so files can be replaced
  Exec('taskkill.exe', '/F /IM "Mawaqit Widget.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM widget.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure ClearMarkOfTheWeb(const FilePath: string);
begin
  // Removes the "downloaded from internet" ADS that SmartScreen often uses.
  // Does NOT override enterprise WDAC / Smart App Control enforcement.
  DeleteFile(FilePath + ':Zone.Identifier');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ClearMarkOfTheWeb(ExpandConstant('{app}\{#MyAppExeName}'));
    ClearMarkOfTheWeb(ExpandConstant('{app}\icon.ico'));
  end;
end;
