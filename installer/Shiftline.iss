; Shiftline installer (Inno Setup 6, https://jrsoftware.org/isinfo.php).
; It copies Shiftline.Plugin.dll into the SimHub folder and puts the loader LED profile into Public Documents\Shiftline.
; Build: first build the plugin in Release (Visual Studio), then compile this file (Inno Setup compiler, or
;   ISCC.exe /DAppVersion=0.2.1 Shiftline.iss). The installer appears in installer\Output.

#ifndef AppVersion
  #define AppVersion "0.2.2"
#endif

[Setup]
AppId={{D7BF378B-87FC-48C1-9290-72760A3E5F1F}
AppName=Shiftline
AppVersion={#AppVersion}
AppPublisher=rauwman
AppPublisherURL=https://github.com/rauwman/Shiftline
DefaultDirName={commonpf32}\SimHub
AppendDefaultDirName=no
DirExistsWarning=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=Shiftline-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE.txt
InfoBeforeFile=info.txt
UninstallDisplayName=Shiftline (SimHub plugin)
UninstallFilesDir={commonappdata}\Shiftline
CloseApplications=no
VersionInfoVersion=0.2.2.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\plugin\Shiftline\bin\Release\Shiftline.Plugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\profiles\Shiftline Loader.ledsprofile"; DestDir: "{commondocs}\Shiftline"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{commondocs}\Shiftline"; Flags: ignoreversion
; The overlays use the fonts Inter and Saira SemiCondensed, built into the DLL. Their licence (SIL Open Font License 1.1)
; has to travel with them.
Source: "..\plugin\Shiftline\Assets\Fonts\OFL-*.txt"; DestDir: "{commondocs}\Shiftline\Licenses"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\Shiftline\Uninstall Shiftline"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\SimHubWPF.exe"; WorkingDir: "{app}"; Description: "Start SimHub again"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: SimHubWasClosed
Filename: "{app}\SimHubWPF.exe"; WorkingDir: "{app}"; Description: "Start SimHub now"; Flags: postinstall nowait skipifsilent runasoriginaluser unchecked; Check: not SimHubWasClosed
Filename: "{win}\explorer.exe"; Parameters: """{commondocs}\Shiftline"""; Description: "Open the folder with the LED profile (import it once in SimHub)"; Flags: postinstall nowait skipifsilent unchecked; Check: IsFreshInstall

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D7BF378B-87FC-48C1-9290-72760A3E5F1F}_is1';

var
  FreshInstall: Boolean;
  AlreadyInstalled: Boolean;
  UninstallChosen: Boolean;
  UninstallCommand: String;
  SimHubClosedByUs: Boolean;
  ModePage: TInputOptionWizardPage;

{ True when SimHub is running (it locks the plugin file). }
function SimHubRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq SimHubWPF.exe" | find /I "SimHubWPF.exe" > nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ Asks SimHub to close and, if it does not within 10 seconds, ends it. Remembers that SimHub was running. }
procedure CloseSimHub;
var
  ResultCode, I: Integer;
begin
  if not SimHubRunning then
    Exit;
  SimHubClosedByUs := True;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM SimHubWPF.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for I := 1 to 20 do
  begin
    if not SimHubRunning then
      Exit;
    Sleep(500);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SimHubWPF.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

function ParamExists(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  AlreadyInstalled := RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallCommand);
end;

{ The text of the first page tells whether this setup repairs, updates or goes back to an older version. }
function InstalledVersionText: String;
var
  Installed, ThisVersion: Int64;
  Text: String;
begin
  Result := 'Shiftline is already installed.';
  if RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', Text) and StrToVersion(Text, Installed) and StrToVersion('{#AppVersion}', ThisVersion) then
  begin
    if ComparePackedVersion(Installed, ThisVersion) = 0 then
      Result := 'Shiftline {#AppVersion} is already installed.'
    else if ComparePackedVersion(Installed, ThisVersion) < 0 then
      Result := 'Shiftline ' + Text + ' is installed, this setup has version {#AppVersion}.'
    else
      Result := 'A newer version (' + Text + ') is installed, this setup has the older version {#AppVersion}.';
  end;
end;

{ The Next button reads "Uninstall" while the uninstall choice is selected. }
procedure UpdateNextCaption;
begin
  if AlreadyInstalled and (WizardForm.CurPageID = ModePage.ID) and ModePage.Values[1] then
    WizardForm.NextButton.Caption := 'Uninstall'
  else
    WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);
end;

procedure ModeClickCheck(Sender: TObject);
begin
  UpdateNextCaption;
end;

{ When Shiftline is already installed, the first page offers to update (or repair) it or to uninstall it. }
procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome, 'Shiftline is already installed', InstalledVersionText, 'What do you want to do?', True, False);
  ModePage.Add('Update or repair: install this version over the existing one (settings and presets are kept)');
  ModePage.Add('Uninstall Shiftline');
  ModePage.Values[0] := True;
  ModePage.CheckListBox.OnClickCheck := @ModeClickCheck;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = ModePage.ID) and (not AlreadyInstalled) then
    Result := True;
  { An update goes into the folder of the existing installation. }
  if AlreadyInstalled and (PageID = wpSelectDir) then
    Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  UpdateNextCaption;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  { Closing the wizard after "Uninstall" needs no question. }
  if UninstallChosen then
    Confirm := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    FreshInstall := not FileExists(ExpandConstant('{app}\Shiftline.Plugin.dll'));
end;

{ SimHub locks the plugin file, so it has to be closed before anything is copied. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if SimHubRunning then
  begin
    if MsgBox('SimHub is running and has to be closed to install Shiftline. Close SimHub now?', mbConfirmation, MB_YESNO) = IDYES then
      CloseSimHub
    else
      Result := 'Close SimHub and start the installation again.';
  end;
end;

function SimHubWasClosed: Boolean;
begin
  Result := SimHubClosedByUs;
end;

function IsFreshInstall: Boolean;
begin
  Result := FreshInstall;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if AlreadyInstalled and (CurPageID = ModePage.ID) and ModePage.Values[1] then
  begin
    { The normal Windows uninstaller does the work, with its own questions, progress and final message. }
    UninstallChosen := True;
    Exec(RemoveQuotes(UninstallCommand), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    Result := False;
    WizardForm.Close;
    Exit;
  end;
  if CurPageID = wpSelectDir then
  begin
    if not FileExists(WizardDirValue + '\SimHubWPF.exe') then
      Result := MsgBox('SimHubWPF.exe was not found in this folder, so it does not look like a SimHub folder. Install here anyway?',
        mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

{ SimHub locks the plugin file, so it has to be closed before the plugin is removed. }
function InitializeUninstall: Boolean;
begin
  Result := True;
  if SimHubRunning then
  begin
    if (not UninstallSilent) and
       (MsgBox('SimHub is running and will be closed so that Shiftline can be removed. Continue?', mbConfirmation, MB_OKCANCEL) <> IDOK) then
      Result := False
    else
      CloseSimHub;
  end;
end;

{ SimHub was running when the uninstall began: offer to start it again (through Explorer, so it does not run as administrator). }
procedure DeinitializeUninstall;
var
  ResultCode: Integer;
begin
  if SimHubClosedByUs and not UninstallSilent and
     (MsgBox('Shiftline has been removed. Start SimHub again?', mbConfirmation, MB_YESNO) = IDYES) then
    Exec(ExpandConstant('{win}\explorer.exe'), '"' + ExpandConstant('{app}\SimHubWPF.exe') + '"', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
  DeleteData: Boolean;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if UninstallSilent then
      DeleteData := ParamExists('/DELETEDATA')
    else
      DeleteData := MsgBox('Also delete your Shiftline settings, presets and backups?' + #13#10 + #13#10 +
        'This cannot be undone. Choose No to keep them for a later installation.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    if DeleteData then
    begin
      DataFolder := ExpandConstant('{app}\PluginsData');
      DelTree(DataFolder + '\Shiftline', True, True, True);
      DeleteFile(DataFolder + '\Common\Shiftline.GeneralSettings.json');
    end;
  end;
end;
