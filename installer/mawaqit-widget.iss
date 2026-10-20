; Inno Setup 6 script — builds MawaqitWidget-Setup.exe
; Compile with:  ISCC.exe installer\mawaqit-widget.iss
; Or:            .\scripts\build-installer.ps1
;
; Expects release binary at dist\widget.exe (from build-release.ps1).

#define MyAppName "Mawaqit Widget"
#define MyAppVersion "0.1.6"
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
var
  DownloadPage: TDownloadWizardPage;

function WebView2Installed: Boolean;
var
  Pv: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv)
     and (Pv <> '') and (Pv <> '0.0.0.0') then
    Result := True;
  if not Result then
    if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv)
       and (Pv <> '') and (Pv <> '0.0.0.0') then
      Result := True;
  if not Result then
    if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv)
       and (Pv <> '') and (Pv <> '0.0.0.0') then
      Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    'Microsoft Edge WebView2',
    'The mosque map needs WebView2. Setup will download it if it is missing.',
    nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if not WebView2Installed then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        except
          MsgBox('Could not install WebView2 automatically. You can still search mosques by name; the map needs WebView2 from Microsoft.', mbInformation, MB_OK);
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

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
