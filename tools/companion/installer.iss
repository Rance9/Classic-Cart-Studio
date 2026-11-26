; Inno Setup script — Classic Cart Studio for Windows (per-user install, no administrator rights).
; Build: ISCC.exe /DAppVersion=0.11.12 /DSourceDir=..\..\build\dist\ClassicCartStudioBackend /DOutDir=..\..\build\release /DWorkbenchDir=..\..\build\workbench-host installer.iss
; The frozen backend tree (SourceDir) also carries ClassicCartStudio.exe and the WebView2 assemblies, copied there by the release workflow. The wizard's
; pictures (tools/publish/make_installer_art.py) and the setup's icon come from the repository, relative to this script, like the legal files.
; Needs Inno Setup 6.7.0 or later (the dark style's hidebevels, WizardBackColor and WizardBackImageFile).
; Stable technical identifiers are kept on purpose (upgrade compatibility with the earlier "companion" releases): AppId and the install
; folder; the program folder, the backend executable and the data folder carry the product name since 0.3.2.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\dist\ClassicCartStudioBackend"
#endif
#ifndef OutDir
  #define OutDir "..\..\build\release"
#endif
; Microsoft's Evergreen Standalone WebView2 Runtime installers and their record (tools/publish/webview2_prereq.py writes this folder)
#ifndef WebView2Dir
  #define WebView2Dir "..\..\build\webview2"
#endif
; CCS Console Workbench's single-file program for x64 and Arm64 Windows (x64\ and arm64\, tools/console-backup/native-host/build_host.sh), byte
; for byte the files its own release publishes: Setup installs the one for this computer as {app}\workbench\CCS-Console-Workbench.exe, and
; Classic Cart Studio starts it there as its own program (tools/desktop/ClassicCartStudio/WorkbenchLauncher.cs)
#ifndef WorkbenchDir
  #define WorkbenchDir "..\..\build\workbench-host"
#endif
#define WorkbenchExe "CCS-Console-Workbench.exe"
; while any of these exists, Setup and the uninstaller change nothing (InitializeSetup, PrepareToInstall, InitializeUninstall): the installed
; Workbench window, any window of the Workbench file installed here (tools/console-backup/ccs-backup-utility/InstalledLaunch.cs) and the
; console lock every console task holds, whichever program runs it
; (tools/console-backup/CcsBackup/Engine/ConsoleLock.cs). Nothing is ever closed or ended to replace a file while a console task runs.
#define WorkbenchMutexes "Local\CCS.ConsoleWorkbench.Installed,Local\CCS.ConsoleWorkbench.InstalledFile,Local\CCS.ConsoleOperation"
#define AppName "Classic Cart Studio"
#define AppExe "ClassicCartStudio.exe"
#define BackendExe "ClassicCartStudioBackend.exe"
; held by every Classic Cart Studio window, whatever its data folder (tools/desktop/ClassicCartStudio/Program.cs MutexName)
#define StudioMutex "Local\ClassicCartStudio.Main"
#define AppPublisher "Rance9 LLC"
#define AppURL "https://classiccartstudio.com/"
#define RepoURL "https://github.com/Rance9/Classic-Cart-Studio"
#if Ver < EncodeVer(6, 7, 0)
  #error Inno Setup 6.7.0 or later is required (WizardStyle dark with hidebevels, WizardBackColor, WizardBackImageFile)
#endif

[Setup]
#ifdef CCS_SIGN
; A signed release (tools/publish/signing.py iscc-args defines the "ccs" SignTool on ISCC's command line): Inno Setup signs the uninstaller,
; its own temporary copies and the setup itself, in that order. An unsigned build has no SignTool and says so in its release information.
SignTool=ccs
SignedUninstaller=yes
#endif
AppId={{7E4C1C3A-6B0C-4B59-9F3D-2A1D5C0E9B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#RepoURL}/issues
AppUpdatesURL={#RepoURL}/releases
DefaultDirName={localappdata}\Programs\ClassicCartStudio
DisableDirPage=yes
DisableProgramGroupPage=yes
; per-user only: always the non-administrative install mode, with no install-mode dialog and no /ALLUSERS override
; (PrivilegesRequiredOverridesAllowed is left at its default, none); program files, shortcuts and registrations are all per user
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=ClassicCartStudio-Setup-{#AppVersion}
SetupIconFile=..\publish\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
; the look: dark, the title bar included, no bevel lines; the Welcome and Finished pages' picture, the CCS mark at
; the top right of every other page and the background, each at six display scalings (Setup picks the one that fits)
WizardStyle=modern dark includetitlebar hidebevels
WizardSizePercent=110
WizardImageFile=..\publish\assets\installer\wizard-panel-100.png,..\publish\assets\installer\wizard-panel-125.png,..\publish\assets\installer\wizard-panel-150.png,..\publish\assets\installer\wizard-panel-175.png,..\publish\assets\installer\wizard-panel-200.png,..\publish\assets\installer\wizard-panel-250.png
WizardSmallImageFile=..\publish\assets\installer\wizard-wordmark-100.png,..\publish\assets\installer\wizard-wordmark-125.png,..\publish\assets\installer\wizard-wordmark-150.png,..\publish\assets\installer\wizard-wordmark-175.png,..\publish\assets\installer\wizard-wordmark-200.png,..\publish\assets\installer\wizard-wordmark-250.png
WizardBackImageFile=..\publish\assets\installer\wizard-back-100.png,..\publish\assets\installer\wizard-back-125.png,..\publish\assets\installer\wizard-back-150.png,..\publish\assets\installer\wizard-back-175.png,..\publish\assets\installer\wizard-back-200.png,..\publish\assets\installer\wizard-back-250.png
WizardBackColor=#151220
ShowLanguageDialog=no
DisableReadyPage=yes
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=no
UsePreviousGroup=no
ChangesAssociations=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}

[Messages]
; every page in plain words (no technical terms anywhere in the installer)
SetupWindowTitle=Install %1
WelcomeLabel1=Welcome to Classic Cart Studio
WelcomeLabel2=Classic Cart Studio helps you choose your NES games, organize them into collections and put them on your NES Classic.%n%nIt installs just for you on this computer. Your games and collections stay on this computer.
ClickNext=Click Next to get started.
ExitSetupTitle=Stop Installing?
ExitSetupMessage=Classic Cart Studio is not installed yet. You can run this installer again anytime.%n%nStop now?
WizardSelectTasks=Shortcuts
SelectTasksDesc=Where would you like to find Classic Cart Studio?
SelectTasksLabel2=Classic Cart Studio will be in your Start menu. You can also put it on your desktop. Then click Install.
WizardPreparing=Getting Ready
PreparingDesc=Getting ready to install Classic Cart Studio.
ApplicationsFound=Classic Cart Studio is open. It needs to close so it can be updated.
ApplicationsFound2=Classic Cart Studio is open. It needs to close so it can be updated.
CloseApplications=&Close it for me (recommended)
DontCloseApplications=&Don't close it (restart the computer later to finish)
WizardInstalling=Installing
InstallingLabel=Please wait while Classic Cart Studio is installed. This can take a minute or two.
StatusClosingApplications=Closing Classic Cart Studio...
StatusCreateDirs=Getting ready...
StatusExtractFiles=Copying files...
StatusCreateIcons=Adding shortcuts...
StatusCreateIniEntries=Finishing up...
StatusCreateRegistryEntries=Finishing up...
StatusRegisterFiles=Finishing up...
StatusSavingUninstall=Finishing up...
StatusRunProgram=Finishing up...
StatusRestartingApplications=Starting Classic Cart Studio...
StatusRollback=Undoing changes...
FinishedHeadingLabel=Classic Cart Studio is ready
FinishedLabel=Classic Cart Studio is installed. You will find it in your Start menu.
FinishedLabelNoIcons=Classic Cart Studio is installed. You will find it in your Start menu.
ClickFinish=Click Finish to close this window.
UninstallAppFullTitle=Remove %1
ConfirmUninstall=Remove Classic Cart Studio from this computer?%n%nYour collections, games and backups are kept.
UninstallStatusLabel=Please wait while Classic Cart Studio is removed.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; checked unless the person unchecked it in an earlier installation
Name: "desktopicon"; Description: "Put Classic Cart Studio on my &desktop"

[Files]
; FIRST, the small files the pages need before anything is installed (extracted for them): with solid compression a file is reached only
; after everything before it is unpacked, so placed after the program they held the first page back for about 20 s.
; the wordmark at every display scale: the page header loads the one drawn for this display's scale (ShowWordmark)
Source: "..\publish\assets\installer\wizard-wordmark-*.png"; Flags: dontcopy
; the Installation Terms (tools/publish/legal_terms.py writes this copy from the one maintained source) and the licenses: shown by the
; Before You Install page (extracted for it, nothing installed yet) and installed beside the program, readable offline
Source: "..\publish\assets\legal\INSTALLATION-TERMS.txt"; DestName: "ccs-terms.txt"; Flags: dontcopy
Source: "..\..\LICENSE"; DestName: "ccs-license.txt"; Flags: dontcopy
Source: "..\..\THIRD_PARTY_NOTICES.md"; DestName: "ccs-notices.txt"; Flags: dontcopy
; what the optional usage reports collect (the setup's "What Is Sent?" and a copy beside the program)
Source: "..\publish\assets\legal\USAGE-REPORTS.txt"; DestName: "ccs-usage.txt"; Flags: dontcopy
Source: "..\publish\assets\legal\INSTALLATION-TERMS.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}\legal"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\legal"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion
Source: "..\publish\assets\legal\USAGE-REPORTS.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
; the Microsoft Edge WebView2 Runtime: before anything is installed, Setup runs the app itself in its runtime-check mode
; (Runtime.cs; these few files are extracted for it), and only when no usable Runtime is found Microsoft's own offline installer for this
; computer's architecture, silently. Microsoft's installers are stored as they are, in their own part of this setup file, so an ordinary
; installation never unpacks them. What Setup carries is recorded in legal\webview2-prereq.json.
Source: "{#SourceDir}\{#AppExe}"; Flags: dontcopy
Source: "{#SourceDir}\{#AppExe}.config"; Flags: dontcopy
Source: "{#SourceDir}\Microsoft.Web.WebView2.Core.dll"; Flags: dontcopy
Source: "{#SourceDir}\Microsoft.Web.WebView2.Wpf.dll"; Flags: dontcopy
Source: "{#SourceDir}\WebView2Loader.dll"; Flags: dontcopy
Source: "{#WebView2Dir}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: dontcopy nocompression solidbreak
Source: "{#WebView2Dir}\MicrosoftEdgeWebView2RuntimeInstallerARM64.exe"; Flags: dontcopy nocompression
Source: "{#WebView2Dir}\webview2-prereq.json"; DestDir: "{app}\legal"; Flags: ignoreversion solidbreak
; the program (SourceDir's workbench\ folder holds the x64 Workbench only for the release build's self-test: the one for this computer comes next)
Source: "{#SourceDir}\*"; Excludes: "\workbench\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; CCS Console Workbench, as its own release publishes it: the Arm64 program on Arm64 Windows, the x64 program everywhere else
Source: "{#WorkbenchDir}\x64\{#WorkbenchExe}"; DestDir: "{app}\workbench"; Check: not IsArm64; Flags: ignoreversion
Source: "{#WorkbenchDir}\arm64\{#WorkbenchExe}"; DestDir: "{app}\workbench"; Check: IsArm64; Flags: ignoreversion

[Icons]
; one Start menu entry, no folder and no website shortcut
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\ClassicCartStudio.ico"; Comment: "Build and prepare your NES Classic game collection"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; classiccartstudio:// links (per user), the application's only link type: the website opens the application on a published collection (classiccartstudio://share/<id>), on
; a collection or a .ccs package it handed to this computer (handoff?offer=, package?offer=), or just brings it forward (open). The
; application validates every link and accepts identifiers only, never commands, paths or data.
Root: HKCU; Subkey: "Software\Classes\classiccartstudio"; ValueType: string; ValueName: ""; ValueData: "URL:Classic Cart Studio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\classiccartstudio"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\classiccartstudio\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\classiccartstudio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Flags: uninsdeletekey
; .ccs collection packages (per user). The file type shows the application's own icon (the icon resource of ClassicCartStudio.exe).
; The extension's default is set only when none exists, so a default the person chose is never overridden; Classic Cart Studio is
; always offered under Open With.
Root: HKCU; Subkey: "Software\Classes\ClassicCartStudio.Collection"; ValueType: string; ValueName: ""; ValueData: "Classic Cart Studio Collection"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ClassicCartStudio.Collection"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Classic Cart Studio Collection"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ClassicCartStudio.Collection\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ClassicCartStudio.Collection\shell\open"; ValueType: string; ValueName: ""; ValueData: "Open with Classic Cart Studio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\ClassicCartStudio.Collection\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.ccs"; ValueType: string; ValueName: ""; ValueData: "ClassicCartStudio.Collection"; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\Classes\.ccs"; ValueType: string; ValueName: "Content Type"; ValueData: "application/vnd.classiccartstudio.collection+zip"; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\Classes\.ccs\OpenWithProgids"; ValueType: string; ValueName: "ClassicCartStudio.Collection"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".ccs"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start Classic Cart Studio now"; Flags: nowait postinstall skipifsilent
; an update the app started (Update Now): quiet, so it starts the app again itself
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: UpdateRunQuiet

[InstallDelete]
; the Start menu folder of earlier versions (app shortcut + website shortcut): replaced by the single entry above
Type: files; Name: "{userprograms}\Classic Cart Studio\Classic Cart Studio.lnk"
Type: files; Name: "{userprograms}\Classic Cart Studio\Classic Cart Studio website.url"
Type: dirifempty; Name: "{userprograms}\Classic Cart Studio"
; the bundled program files are replaced as a whole (0.5.0: the interface, library and artwork live here; files an earlier version
; carried must not linger). User data is not in {app}: it stays in %LOCALAPPDATA%\ClassicCartStudio.
Type: filesandordirs; Name: "{app}\_internal"
; the Workbench window and its libraries ran inside Classic Cart Studio until 0.11.3: those files beside the program are gone (the Workbench is
; its own program in workbench\ now)
Type: files; Name: "{app}\CcsBackupUi.dll"
Type: files; Name: "{app}\CcsBackup.dll"
Type: files; Name: "{app}\CcsConsole.dll"
Type: files; Name: "{app}\LibUsbDotNet.LibUsbDotNet.dll"
Type: files; Name: "{app}\ccs-par2.dll"

[UninstallDelete]
; program files only — the user's data (%LOCALAPPDATA%\ClassicCartStudio: imported games, personal games, collections, logs) is kept on purpose
Type: filesandordirs; Name: "{app}\_internal"

[Code]
// ---- Before You Install (the terms): after Welcome, before anything is installed. The full Installation Terms, the GNU GPL and the
// third-party notices are readable here (from the setup itself, offline) and can be saved; the agreement box starts unchecked and
// Continue stays unavailable until it is checked. Nothing is recorded or sent: no receipt is kept. A silent setup (/SILENT, /VERYSILENT)
// shows no pages, so no one agrees there: it installs without recording any agreement, and the same terms are installed in legal\
// ---- Optional usage and error reports: a separate page after the terms, its own choice (never part of the agreement). The box starts
// CHECKED, except where an opt-in rule applies ("deselected by default for EU and other nations requiring
// that"): it starts UNCHECKED when this computer's Windows region (its two-letter country) OR its time zone belongs to the EU, the EEA, the
// UK or Switzerland, or when the region cannot be read (UsageStartsChecked). Both are settings, not proof of where the person is; either one
// is enough, so most people there start unchecked even on a computer set up elsewhere. The person can change it on the page and anytime in
// the Studio menu. No network or location lookup, and never the display language. A choice already kept on this computer (usage-reporting.json in the app's data folder: an earlier setup or the
// Studio menu's Usage Reporting) is kept as it is: the page is skipped and nothing is written, so an upgrade, repair or reinstall never
// changes it, and an opt-out stays; any usage-reporting.json already there, even one the app would not accept, is never overwritten (no
// choice there means off, and the Studio menu can set it). The choice is written once the program files are in place, before the app can
// first start; Cancel writes nothing; a silent setup shows no page and writes nothing (no choice means off). The setup itself never sends anything.
// The data folder is %LOCALAPPDATA%\ClassicCartStudio (App.cs DataRoot and common.py _data_root); no other folder is moved or used.
const
  USAGE_SCHEMA = 'ccs-usage-choice/1';
  OBM_CHECKBOXES = 32759;   // TNewCheckListBox: its check width is a quarter of Windows' check-box bitmap
  // the space before every check box (its list's Offset) and between the box and its words, at 100 %: Inno Setup's own is 4 pixels at every
  // display scale, which left the words touching the box (at 200 % the box doubles and the space does not); CheckGap is it at this scale
  CHECK_GAP = 8;
  WORDMARK_W = 100; WORDMARK_H = 26;   // the wordmark's size at 100 % (tools/publish/make_installer_art.py draws it at every scale)
  HORIZON_PCT = 80;   // the background's glowing horizon line, in percent of the window's height (make_installer_art.py back()): no words reach it
  USAGE_LABEL = 'Send optional usage and diagnostic data (recommended)';
  // where the usage box starts unchecked: the EU, the EEA (Iceland, Liechtenstein, Norway), the UK and Switzerland, with their European
  // territories, by Windows region (ISO 3166 code) or by Windows time zone
  OPTIN_REGIONS = ',AT,BE,BG,HR,CY,CZ,DK,EE,FI,FR,DE,GR,HU,IE,IT,LV,LT,LU,MT,NL,PL,PT,RO,SK,SI,ES,SE,IS,LI,NO,GB,CH,GF,GP,MQ,RE,YT,MF,AX,GI,JE,GG,IM,';
  OPTIN_ZONES = ',GMT Standard Time,Greenwich Standard Time,W. Europe Standard Time,Central Europe Standard Time,Romance Standard Time,Central European Standard Time,GTB Standard Time,FLE Standard Time,Azores Standard Time,';
  AGREE_LABEL = 'I agree to the terms and understand the risks.';
var
  TermsPage: TWizardPage; TermsShow: TNewComboBox; TermsText: TNewMemo; TermsAgree: TNewCheckListBox; TermsSave: TNewButton;
  TermsFiles: array[0..2] of String; TermsNames: array[0..2] of String;
  UsagePage: TWizardPage; UsageBox: TNewCheckListBox; UsageStored: String;   // UsageStored: 'on' / 'off' kept on this computer, 'file' another file there, '' none
  LabelIndent: Integer;   // where a check box's own words start at this display's scale: text that belongs to a check box lines up with them
  CheckGap: Integer;      // CHECK_GAP at this display's scale (RoomyChecks)

type
  TWinBitmap = record bmType, bmWidth, bmHeight, bmWidthBytes: Longint; bmPlanes, bmBitsPixel: Word; bmBits: Longint; end;

function LoadBitmapW(Inst: Longint; Name: Longint): Longint; external 'LoadBitmapW@user32.dll stdcall';
function GetObjectW(Obj: Longint; Size: Integer; var Bm: TWinBitmap): Integer; external 'GetObjectW@gdi32.dll stdcall';
function DeleteObject(Obj: Longint): Boolean; external 'DeleteObject@gdi32.dll stdcall';
function GetUserDefaultGeoName(Buf: String; Count: Integer): Integer; external 'GetUserDefaultGeoName@kernel32.dll stdcall delayload';

function UsageStartsChecked: Boolean;   // checked, except by Windows region or time zone in OPTIN_REGIONS / OPTIN_ZONES, or an unreadable region
var Buf, Region, Zone: String; N: Integer;
begin
  Region := '';
  try
    Buf := StringOfChar(' ', 16); N := GetUserDefaultGeoName(Buf, 16);   // the count includes the closing null
    if N > 1 then Region := Uppercase(Copy(Buf, 1, N - 1));
  except
    Region := '';
  end;
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\TimeZoneInformation', 'TimeZoneKeyName', Zone) then Zone := '';
  Result := (Region <> '') and (Pos(',' + Region + ',', OPTIN_REGIONS) = 0) and ((Zone = '') or (Pos(',' + Zone + ',', OPTIN_ZONES) = 0));
end;

function CheckLabelIndent: Integer;   // as TNewCheckListBox places a check box's words in the lists here (WantTabs, Offset = CheckGap): the
var H: Longint; Bm: TWinBitmap;        // space, the box, the space again (29 px at 100 %, 57 at 200 %)
begin
  Result := ScaleX(13) + 2 * CheckGap;   // (only if Windows cannot say)
  H := LoadBitmapW(0, OBM_CHECKBOXES);
  if H <> 0 then begin
    if GetObjectW(H, 24, Bm) > 0 then Result := Bm.bmWidth div 4 + 2 * CheckGap;
    DeleteObject(H);
  end;
end;

procedure RoomyChecks(L: TNewCheckListBox);   // CheckGap before the box and between the box and its words, at this display's scale
begin
  L.Offset := CheckGap;
end;

function ReadUtf8(const Path: String): String;   // the copies are UTF-8 (the terms with a byte-order mark); lines end in CR LF for the box
var A: AnsiString;
begin
  Result := '';
  if LoadStringFromFile(Path, A) then begin
    Result := UTF8Decode(A);
    if (Length(Result) > 0) and (Ord(Result[1]) = $FEFF) then Delete(Result, 1, 1);
    StringChangeEx(Result, #13#10, #10, True); StringChangeEx(Result, #10, #13#10, True);
  end;
end;

procedure TermsShowChange(Sender: TObject);
begin
  TermsText.Text := ReadUtf8(ExpandConstant('{tmp}\') + TermsFiles[TermsShow.ItemIndex]);
  TermsText.SelStart := 0;
end;

procedure TermsSaveClick(Sender: TObject);
var F: String;
begin
  F := TermsNames[TermsShow.ItemIndex];
  if GetSaveFileName('Save a copy of ' + TermsShow.Text, F, ExpandConstant('{userdocs}'), 'Text files (*.txt)|*.txt|All files (*.*)|*.*', 'txt') then
    if not CopyFile(ExpandConstant('{tmp}\') + TermsFiles[TermsShow.ItemIndex], F, False) then
      MsgBox('The copy could not be saved there. Choose another folder.', mbError, MB_OK);
end;

procedure TermsAgreeClick(Sender: TObject);
begin
  WizardForm.NextButton.Enabled := TermsAgree.Checked[0];
end;

function UsageDataDir: String;   // the app's data folder
begin
  Result := ExpandConstant('{localappdata}\ClassicCartStudio');
end;

function UsageChoiceKept: String;   // 'on' or 'off' when usage-reporting.json holds a valid choice (as the app reads it), 'file' when it holds
var A: AnsiString; S: String;        // anything else (kept as it is, never overwritten), '' when there is none
begin
  Result := '';
  if not FileExists(UsageDataDir + '\usage-reporting.json') then Exit;
  Result := 'file';
  if LoadStringFromFile(UsageDataDir + '\usage-reporting.json', A) then begin
    S := UTF8Decode(A); StringChangeEx(S, ' ', '', True); StringChangeEx(S, #9, '', True); StringChangeEx(S, #13, '', True); StringChangeEx(S, #10, '', True);
    if Pos('"schema":"' + USAGE_SCHEMA + '"', S) > 0 then begin
      if (Pos('"choice":"on"', S) > 0) and (Pos('"choice":"off"', S) = 0) then Result := 'on'
      else if (Pos('"choice":"off"', S) > 0) and (Pos('"choice":"on"', S) = 0) then Result := 'off';
    end;
  end;
end;

function WrappedHeight(Parent: TWinControl; const S: String; W: Integer): Integer;   // how tall S is when wrapped to W in Parent's font
var T: TNewStaticText;
begin
  T := TNewStaticText.Create(Parent);
  try
    T.Visible := False; T.Parent := Parent; T.AutoSize := True; T.WordWrap := True; T.Width := W; T.Caption := S; Result := T.Height;
  finally
    T.Free;
  end;
end;

procedure FitCheckRow(L: TNewCheckListBox; const S: String);   // a one-box list exactly as tall as its words wrap at this display's scale
var H: Integer;
begin
  H := WrappedHeight(L.Parent, S, L.ClientWidth - LabelIndent - CheckGap);   // the list's own text width (its MeasureItem)
  L.MinItemHeight := H + ScaleY(6); L.Height := H + ScaleY(10);
end;

function SafeBottom(Surface: TWinControl): Integer;   // the lowest point words may reach on a page: a margin above the background's horizon line
var C: TControl; Y: Integer;
begin
  Y := 0; C := Surface;
  while C.Parent <> nil do begin Y := Y + C.Top; C := C.Parent; end;   // the page's top within the window
  Result := WizardForm.ClientHeight * HORIZON_PCT div 100 - ScaleY(10) - Y;
  if Result > Surface.Height then Result := Surface.Height;
end;

procedure ShowWordmark;   // the wordmark at the top right of every page after Welcome: the picture drawn for this display's scale, at its own size
var Img: TBitmapImage; W, H, Gap, Pct, I: Integer; F: String; Scales: array[0..5] of Integer;
begin
  Img := WizardForm.WizardSmallBitmapImage; W := ScaleX(WORDMARK_W); H := ScaleY(WORDMARK_H);
  Scales[0] := 100; Scales[1] := 125; Scales[2] := 150; Scales[3] := 175; Scales[4] := 200; Scales[5] := 250; Pct := 250;
  for I := 0 to 5 do
    if (WORDMARK_W * Scales[I] + 50) div 100 >= W then begin Pct := Scales[I]; Break; end;   // the smallest drawn at least this wide
  F := 'wizard-wordmark-' + IntToStr(Pct) + '.png'; ExtractTemporaryFile(F);
  Gap := WizardForm.MainPanel.ClientWidth - (Img.Left + Img.Width);   // Setup's own right margin for this picture, kept
  Img.Top := Img.Top + (Img.Height - H) div 2; Img.Width := W; Img.Height := H; Img.Left := WizardForm.MainPanel.ClientWidth - Gap - W;
  Img.Stretch := True; Img.PngImage.LoadFromFile(ExpandConstant('{tmp}\' + F));
  WizardForm.PageNameLabel.Width := Img.Left - WizardForm.PageNameLabel.Left - ScaleX(8);   // the page's title and subtitle end before it
  WizardForm.PageDescriptionLabel.Width := Img.Left - WizardForm.PageDescriptionLabel.Left - ScaleX(8);
end;

procedure UsageDetailsClick(Sender: TObject);   // "View Details": the bundled text, offline
var F: TSetupForm; M: TNewMemo; B: TNewButton;
begin
  F := CreateCustomForm(ScaleX(560), ScaleY(440), False, True);
  try
    F.Caption := 'Usage and Diagnostics: Details';
    F.ClientWidth := WizardForm.ClientWidth; F.ClientHeight := WizardForm.ClientHeight;
    F.Position := poDesigned; F.Left := WizardForm.Left + (WizardForm.Width - F.Width) div 2; F.Top := WizardForm.Top + (WizardForm.Height - F.Height) div 2;   // the setup's own size, over it (at 200% scaling the requested size came out about 2.5 times larger)
    B := TNewButton.Create(F); B.Parent := F; B.Caption := 'Close'; B.Width := ScaleX(90); B.Height := ScaleY(26);
    B.Left := F.ClientWidth - B.Width - ScaleX(12); B.Top := F.ClientHeight - B.Height - ScaleY(12); B.ModalResult := mrOk; B.Cancel := True; B.Default := True;
    M := TNewMemo.Create(F); M.Parent := F; M.Left := ScaleX(12); M.Top := ScaleY(12); M.Width := F.ClientWidth - ScaleX(24); M.Height := B.Top - ScaleY(24);
    M.ReadOnly := True; M.WordWrap := True; M.ScrollBars := ssVertical; M.Text := ReadUtf8(ExpandConstant('{tmp}\ccs-usage.txt'));
    F.ActiveControl := M; F.ShowModal;
  finally
    F.Free;
  end;
end;

procedure CreateUsagePage;
var Desc: TNewStaticText; More: TNewButton;
begin
  ExtractTemporaryFile('ccs-usage.txt'); UsageStored := UsageChoiceKept;
  UsagePage := CreateCustomPage(TermsPage.ID, 'Usage and Diagnostics', 'Choose whether to share usage and diagnostic data.');
  UsageBox := TNewCheckListBox.Create(UsagePage);
  UsageBox.Parent := UsagePage.Surface; UsageBox.Left := 0; UsageBox.Top := 0; UsageBox.Width := UsagePage.SurfaceWidth; UsageBox.Height := ScaleY(22);
  UsageBox.Flat := True; UsageBox.BorderStyle := bsNone; UsageBox.ParentColor := True; RoomyChecks(UsageBox);
  UsageBox.WantTabs := True;   // a plain check box on the page: no list background or selection bar on the dark style (as the terms' agreement)
  UsageBox.AddCheckBox(USAGE_LABEL, '', 0, UsageStartsChecked, True, False, False, nil);   // checked, or unchecked where opt-in applies (see above)
  FitCheckRow(UsageBox, USAGE_LABEL);   // as tall as its words wrap (a one-line row cut them off)
  Desc := TNewStaticText.Create(UsagePage);
  Desc.Parent := UsagePage.Surface; Desc.Left := LabelIndent; Desc.Top := UsageBox.Top + UsageBox.Height + ScaleY(6); Desc.Width := UsagePage.SurfaceWidth - LabelIndent; Desc.WordWrap := True; Desc.AutoSize := True;
  Desc.Caption := 'Help improve Classic Cart Studio''s performance and reliability. Reports contain no personal identifiers and are never used for advertising or marketing. You can change this setting at any time under Studio ' + #$2192 + ' Usage Reporting.';
  More := TNewButton.Create(UsagePage);
  More.Parent := UsagePage.Surface; More.Caption := '&View Details'; More.Width := ScaleX(150); More.Height := ScaleY(26);
  More.Left := LabelIndent; More.Top := Desc.Top + Desc.Height + ScaleY(14); More.OnClick := @UsageDetailsClick;
  UsageBox.TabOrder := 0; More.TabOrder := 1;
end;

// ---- The Microsoft Edge WebView2 Runtime: Classic Cart Studio shows its window with it. Before anything is installed,
// Setup runs the app itself in its runtime-check mode (Runtime.cs: Microsoft's discovery, else a verified Microsoft Runtime folder, and a
// real start of the Runtime). Only when no usable Runtime is found does Setup run Microsoft's Evergreen Standalone installer for this
// computer's architecture (only when none was found or it is too old), bundled in this file (no download), with Microsoft's silent switches (/silent /install): no Microsoft window
// appears; this page says what is happening. Setup runs without administrator rights, so Microsoft's installer adds the Runtime for this
// user (or Windows' existing per-machine Runtime is updated by Microsoft's own updater); Windows' security prompts are never bypassed.
// Then the check runs again. If no usable Runtime is there afterwards, Setup stops honestly with nothing of Classic Cart Studio installed
// and says why (it does not claim nothing changed: Microsoft's installer may have done part of its work). Both checks and Microsoft's exit
// code are kept in the data folder's logs\setup-webview2-runtime.json (the messages name it in short words; the codes stay in the log).
// Cancel is on every page before Install. The Getting Ready page is Inno Setup's progress page, which shows no buttons: the check takes
// seconds, and Microsoft's installer is waited for (stopping it halfway could leave a shared Windows part broken).
// The Runtime is shared by many apps: removing Classic Cart Studio never removes it.
var
  RuntimePage: TOutputMarqueeProgressWizardPage; RuntimeLog: String;

function RuntimeCheck(const Tag: String; var Json: String): Integer;
var Code: Integer; OutF: String; A: AnsiString;
begin
  OutF := ExpandConstant('{tmp}\ccs-runtime-' + Tag + '.json'); DeleteFile(OutF);
  if not Exec(ExpandConstant('{tmp}\{#AppExe}'), '--runtime-check "' + OutF + '"', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) then Code := -1;
  Json := '';
  if LoadStringFromFile(OutF, A) then Json := UTF8Decode(A);
  Log('WebView2 Runtime check (' + Tag + '): ' + IntToStr(Code) + ' ' + Json);
  Result := Code;
end;

// where the details are, in words that wrap on the page (the full path did not fit at 200 %)
function RuntimeLogNote: String;
begin
  Result := '';
  if RuntimeLog <> '' then Result := ' The details are saved in setup-webview2-runtime.json, in the logs folder of %LOCALAPPDATA%\' + ExtractFileName(UsageDataDir) + '.';
end;

procedure RuntimeKeepLog(const Before, After, Installer: String; Code: Integer);
var Dir: String;
begin
  Dir := UsageDataDir + '\logs';
  if not ForceDirectories(Dir) then Exit;
  RuntimeLog := Dir + '\setup-webview2-runtime.json';
  SaveStringToFile(RuntimeLog, '{"schema":"ccs-setup-runtime/1","setup":"{#AppVersion}","before":' + Before + ',"installer":"' + Installer + '","installer_exit":' + IntToStr(Code) + ',"after":' + After + '}', False);
end;

function RuntimeUsable(Code: Integer): Boolean;
begin
  Result := (Code = 0) or (Code = 1);   // 0: Microsoft's discovery; 1: a verified Microsoft Runtime folder (Runtime.cs)
end;

// ---- CCS Console Workbench (installed with Classic Cart Studio, its own program): while its installed window is open or any console task
// runs (WorkbenchMutexes), Setup and the uninstaller change nothing and end nothing. They ask the person to close Workbench once its task has
// finished (OK checks again, Cancel stops without changes); a silent setup stops without changes. An update the app started (Studio has
// already closed itself) starts Studio again when it stops, so the person is not left without it.
const
  WORKBENCH_OPEN = 'CCS Console Workbench is open, or a console task is running. Wait for the task to finish and close CCS Console Workbench, then click OK. Nothing has been changed yet.';

function WorkbenchOpen: Boolean;
begin
  Result := CheckForMutexes('{#WorkbenchMutexes}');
end;

function WorkbenchClosed(Silent: Boolean): Boolean;   // True once nothing of the Workbench runs; False: the person (or a silent run) stopped
begin
  Result := True;
  while WorkbenchOpen do begin
    if Silent or (MsgBox(WORKBENCH_OPEN, mbInformation, MB_OKCANCEL) <> IDOK) then begin Result := False; Exit; end;
  end;
end;

procedure StartStudioAgain;   // an update the app started could not go ahead: the app the person was using comes back, as it was
var Code: Integer;
begin
  Exec(ExpandConstant('{localappdata}\Programs\ClassicCartStudio\{#AppExe}'), '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

// ---- "Close it for me" (Getting Ready, while Classic Cart Studio is open). Windows' own way of closing programs for Setup cannot close the
// app's background program, which has no window, so on its own it closed nothing and Setup reported that it could not close the app. Setup
// therefore closes the app itself, the way the window's close button does: each visible window of a program in this installation's folder
// (the app's window, a game window it opened) is asked to close. The app ends its background program as it closes, and asks first when a
// game is open. Setup waits for that, so this works whichever version of the app is open. Nothing is ended by force.
const
  WM_CLOSE = $0010; GW_OWNER = 4; PROCESS_QUERY_LIMITED_INFORMATION = $1000;
  CLOSE_WAIT_MS = 120000;   // time to answer the app's own question when a game is open
  STILL_OPEN = 'Classic Cart Studio is still open. Close it, then click Next again.';
var
  ClosePage: TOutputMarqueeProgressWizardPage; AppPrefix: String; AppWindowsFound: Integer; AskToClose: Boolean;

function EnumWindows(Callback: Longword; Param: Longint): Boolean; external 'EnumWindows@user32.dll stdcall';
function GetWindowThreadProcessId(Wnd: HWND; var Pid: DWORD): DWORD; external 'GetWindowThreadProcessId@user32.dll stdcall';
function IsWindowVisible(Wnd: HWND): Boolean; external 'IsWindowVisible@user32.dll stdcall';
function GetWindow(Wnd: HWND; Cmd: Cardinal): HWND; external 'GetWindow@user32.dll stdcall';
function OpenProcess(Access: DWORD; Inherit: Boolean; Pid: DWORD): THandle; external 'OpenProcess@kernel32.dll stdcall';
function QueryFullProcessImageNameW(Process: THandle; Flags: DWORD; Name: String; var Size: DWORD): Boolean; external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean; external 'CloseHandle@kernel32.dll stdcall';

function ProgramOf(Wnd: HWND): String;   // the full path of the program that owns a window ('' when Windows does not say)
var Pid, N: DWORD; P: THandle; S: String;
begin
  Result := ''; Pid := 0;
  GetWindowThreadProcessId(Wnd, Pid);
  if Pid = 0 then Exit;
  P := OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, Pid);
  if P = 0 then Exit;
  N := 1024; S := StringOfChar(#0, N);
  if QueryFullProcessImageNameW(P, 0, S, N) then Result := Copy(S, 1, N);
  CloseHandle(P);
end;

function AppWindow(Wnd: HWND; Param: Longint): Boolean;   // each visible window of its own (not a dialog) of a program in this installation
begin
  Result := True;
  if (not IsWindowVisible(Wnd)) or (GetWindow(Wnd, GW_OWNER) <> 0) then Exit;
  if Pos(AppPrefix, Lowercase(ProgramOf(Wnd))) <> 1 then Exit;
  AppWindowsFound := AppWindowsFound + 1;
  if AskToClose then PostMessage(Wnd, WM_CLOSE, 0, 0);
end;

function AppWindows(Close: Boolean): Integer;   // how many there are; Close: ask each one to close
begin
  AppPrefix := Lowercase(AddBackslash(ExpandConstant('{app}'))); AppWindowsFound := 0; AskToClose := Close;
  if Length(AppPrefix) > 4 then EnumWindows(CreateCallback(@AppWindow), 0);
  Result := AppWindowsFound;
end;

function StudioOpen: Boolean;
begin
  Result := (AppWindows(False) > 0) or CheckForMutexes('{#StudioMutex}');
end;

function CloseStudioForUpdate(ShowPage: Boolean): Boolean;   // True once nothing of this installation shows a window and the app has ended
var Waited: Integer;
begin
  Result := not StudioOpen;
  if Result then Exit;
  Log('Closing Classic Cart Studio: ' + IntToStr(AppWindows(True)) + ' window(s) asked to close');
  if ShowPage then ClosePage.Show;
  try
    Waited := 0;
    while (AppWindows(False) > 0) and (Waited < CLOSE_WAIT_MS) do begin
      if ShowPage then ClosePage.Animate;
      Sleep(100); Waited := Waited + 100;
    end;
    // the app's own end follows its window (its background program has stopped by then); a few seconds more at most
    while CheckForMutexes('{#StudioMutex}') and (AppWindows(False) = 0) and (Waited < CLOSE_WAIT_MS + 15000) do begin
      if ShowPage then ClosePage.Animate;
      Sleep(100); Waited := Waited + 100;
    end;
  finally
    if ShowPage then ClosePage.Hide;
  end;
  Result := AppWindows(False) = 0;
  Log('Classic Cart Studio closed for the update: ' + IntToStr(Ord(Result)) + ' after ' + IntToStr(Waited) + ' ms (another app window still holding the app''s lock: ' + IntToStr(Ord(CheckForMutexes('{#StudioMutex}'))) + ')');
end;

function NoCloseApplications: Boolean;   // /NOCLOSEAPPLICATIONS: the person asked Setup not to close programs
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do if CompareText(ParamStr(I), '/NOCLOSEAPPLICATIONS') = 0 then Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Before, After, Code: Integer; J1, J2, F: String; Shown: Boolean;
begin
  Result := ''; Shown := False;
  if WorkbenchOpen then begin   // started since Setup began: still nothing is changed or ended
    if ExpandConstant('{param:CCSUPDATE|0}') = '1' then StartStudioAgain;
    Result := 'Classic Cart Studio was not changed. CCS Console Workbench is open, or a console task is running. Wait for the task to finish and close CCS Console Workbench, then run this installer again.';
    Exit;
  end;
  if WizardSilent and (ExpandConstant('{param:CCSUPDATE|0}') <> '1') and (not NoCloseApplications) and (not CloseStudioForUpdate(False)) then begin
    Result := 'Classic Cart Studio was not changed. ' + STILL_OPEN;
    Exit;
  end;
  ExtractTemporaryFile('{#AppExe}'); ExtractTemporaryFile('{#AppExe}.config'); ExtractTemporaryFile('Microsoft.Web.WebView2.Core.dll');
  ExtractTemporaryFile('Microsoft.Web.WebView2.Wpf.dll'); ExtractTemporaryFile('WebView2Loader.dll');
  if not WizardSilent then begin
    RuntimePage.SetText('Checking for the Microsoft Edge WebView2 Runtime' + #$2026, 'Classic Cart Studio uses this part of Windows to show its window.');
    RuntimePage.Show; Shown := True;
  end;
  try
    Before := RuntimeCheck('before', J1);
    if J1 = '' then J1 := 'null';
    if RuntimeUsable(Before) then begin RuntimeKeepLog(J1, 'null', '', 0); Exit; end;
    if Before = -1 then begin   // the check itself could not run: the WebView2 Runtime is not the problem to fix
      RuntimeKeepLog('null', 'null', '', -1);
      Result := 'Classic Cart Studio was not installed. Setup could not run its own check on this computer. Classic Cart Studio needs Windows 10 version 1903 or later, or Windows 11, with .NET Framework 4.8.';
      Result := Result + RuntimeLogNote;
      Exit;
    end;
    if (Before <> 2) and (Before <> 3) then begin   // a Runtime is there but did not start (4), or the check did not finish (8, 9): Microsoft's installer is only for a missing or too-old Runtime
      RuntimeKeepLog(J1, 'null', '', 0);
      if Before = 4 then Result := 'Classic Cart Studio was not installed. The Microsoft Edge WebView2 Runtime, a free part of Windows from Microsoft that Classic Cart Studio needs, is on this computer but did not start.'
      else Result := 'Classic Cart Studio was not installed. Setup could not finish checking for the Microsoft Edge WebView2 Runtime, a free part of Windows from Microsoft that Classic Cart Studio needs.';
      Result := Result + #13#10#13#10 + 'Restart the computer, then run this installer again.' + RuntimeLogNote;
      Exit;
    end;
    if IsArm64 then F := 'MicrosoftEdgeWebView2RuntimeInstallerARM64.exe' else F := 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe';
    if Shown then begin
      RuntimePage.SetText('Adding the Microsoft Edge WebView2 Runtime' + #$2026, 'This can take a few minutes. Please wait.');   // (one line: this label does not wrap)
      WizardForm.CancelButton.Enabled := False;
    end;
    ExtractTemporaryFile(F);
    if not Exec(ExpandConstant('{tmp}\') + F, '/silent /install', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) then Code := -1;
    Log('Microsoft Edge WebView2 Runtime installer ' + F + ' returned ' + IntToStr(Code));
    if Shown then begin
      WizardForm.CancelButton.Enabled := True;
      RuntimePage.SetText('Checking the Microsoft Edge WebView2 Runtime' + #$2026, '');
    end;
    After := RuntimeCheck('after', J2);
    if J2 = '' then J2 := 'null';
    RuntimeKeepLog(J1, J2, F, Code);
    if RuntimeUsable(After) then Exit;
    Result := 'Classic Cart Studio was not installed. It needs the Microsoft Edge WebView2 Runtime, a free part of Windows from Microsoft, and Setup could not add it.'
      + #13#10#13#10 + 'Restart the computer, then run this installer again.' + RuntimeLogNote;   // Microsoft's code and the checks are in the log
  finally
    if Shown then RuntimePage.Hide;
  end;
end;

// ---- An update the app started (Update Now: /CCSUPDATE=1): the app closes itself after starting this setup. Setup waits up to a minute for
// it to close (its single-instance mutex), and stops without changing anything if it is still open. A quiet update starts the app again.
function UpdateRun: Boolean;
begin
  Result := ExpandConstant('{param:CCSUPDATE|0}') = '1';
end;

function UpdateRunQuiet: Boolean;
begin
  Result := UpdateRun and WizardSilent;
end;

// ---- Microsoft .NET Framework 4.8: Classic Cart Studio needs it (so does the CCS Console Workbench it installs on x64 computers). It is
// part of Windows 10 version 1903 and later and of Windows 11, but Windows 10 version 1809, which this installer accepts, comes with 4.7.2.
// Before anything else Setup reads Windows' own record of it (the Release value). When it is missing, Setup says so and offers Microsoft's
// page for 4.8 in the browser (the Runtime; or the offline Runtime installer, brought from another computer); Continue reads the record
// again. Setup never downloads or installs it itself; Cancel stops with nothing installed; a silent setup stops and logs why. (Without this
// check the WebView2 Runtime check below could not run the app, and would report a WebView2 problem instead.)
// Test hook: CCS_SETUP_SIMULATE_NETFX_RELEASE=<n> takes the LOWER of n and the real value (it can only make the check stricter).
const
  NETFX48_RELEASE = 528040;
  NETFX48_PAGE = 'https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48';
  NETFX48_TYPED = 'dotnet.microsoft.com/download/dotnet-framework/net48';

function NetFxRelease: Cardinal;
var R, S: Cardinal; Sim: String;
begin
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', R) then R := 0;
  Sim := GetEnv('CCS_SETUP_SIMULATE_NETFX_RELEASE');
  if Sim <> '' then begin S := StrToIntDef(Sim, 0); if S < R then R := S; end;
  Result := R;
end;

function NetFxName(R: Cardinal): String;
begin
  if R >= 533320 then Result := '4.8.1 or later'
  else if R >= 528040 then Result := '4.8'
  else if R >= 461808 then Result := '4.7.2'
  else if R >= 461308 then Result := '4.7.1'
  else if R >= 460798 then Result := '4.7'
  else if R >= 394802 then Result := '4.6.2'
  else Result := 'an earlier version';
end;

function NetFxReady(Silent: Boolean): Boolean;   // True once Windows records .NET Framework 4.8 or later; False: the person stopped (or silent)
var R: Cardinal; Again: Boolean; Have, Instruction, Text: String; Code: Integer; Buttons: TArrayOfString;
begin
  Again := False;
  SetArrayLength(Buttons, 2);   // the two command buttons (their words only: the text above explains them); Cancel stays the dialog's own
  Buttons[0] := '&Download Installer';
  Buttons[1] := '&Continue';
  while True do begin
    R := NetFxRelease;
    if R >= NETFX48_RELEASE then begin Result := True; Exit; end;
    Log('.NET Framework 4.8 or later is needed; Windows records the Release value ' + IntToStr(R));
    if Silent then begin Result := False; Exit; end;
    if R = 0 then Have := 'This computer has no .NET Framework 4.' else Have := 'This computer has .NET Framework ' + NetFxName(R) + '.';
    if Again then Instruction := 'Still missing: .NET Framework 4.8' else Instruction := 'Before Classic Cart Studio can install';
    Text := 'Classic Cart Studio needs Microsoft .NET Framework 4.8, a Windows component. Download and install it, then choose Continue. ' + Have + #13#10#13#10 +
      'Download Installer opens Microsoft''s .NET Framework 4.8 page in your web browser. Choose Download .NET Framework 4.8 Runtime there, not the ' +
      'Developer Pack, and run it; Windows asks for an administrator''s permission. If it asks you to restart, restart the computer and run this installer again.' + #13#10#13#10 +
      'No internet on this computer? On another computer, open ' + NETFX48_TYPED + ' and, under Advanced downloads, choose Runtime under Offline installer. ' +
      'Bring that file to this computer, run it, then choose Continue.';
    case TaskDialogMsgBox(Instruction, Text, mbError, MB_YESNOCANCEL, Buttons, 0) of
      IDYES:
        begin
          if not ShellExecAsOriginalUser('open', NETFX48_PAGE, '', '', SW_SHOWNORMAL, ewNoWait, Code) then
            MsgBox('Your web browser could not be opened (' + SysErrorMessage(Code) + '). Open this address in a web browser yourself:' + #13#10#13#10 + NETFX48_PAGE, mbError, MB_OK);
          Again := False;
        end;
      IDNO: Again := True;
    else
      begin Result := False; Exit; end;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  I: Integer;
begin
  Result := True;
  if not NetFxReady(WizardSilent) then begin Result := False; Exit; end;   // nothing installed or changed
  if UpdateRun then begin
    I := 0;
    while CheckForMutexes('Local\ClassicCartStudio.Main') and (I < 120) do begin Sleep(500); I := I + 1; end;
    if CheckForMutexes('Local\ClassicCartStudio.Main') then begin
      if not WizardSilent then MsgBox('Close Classic Cart Studio, then run this installer again to update it.', mbInformation, MB_OK);
      Result := False;
      Exit;
    end;
  end;
  if not WorkbenchClosed(WizardSilent) then begin
    if UpdateRun then StartStudioAgain;
    Result := False;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := WorkbenchClosed(UninstallSilent);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = UsagePage.ID) and (UsageStored <> '');   // a choice already kept here stays as it is
end;

procedure CurStepChanged(CurStep: TSetupStep);   // after the program files, before the app can first start ([Run] comes later)
var Dir: String;
begin
  // (Getting Ready's Next closes the app; this only catches one opened again since then, before Windows' own closing step)
  if (CurStep = ssInstall) and (not WizardSilent) and WizardForm.PreparingYesRadio.Visible and WizardForm.PreparingYesRadio.Checked and StudioOpen then
    CloseStudioForUpdate(False);
  if (CurStep = ssPostInstall) and (not WizardSilent) and (UsageStored = '') then begin
    Dir := UsageDataDir;
    if FileExists(Dir + '\usage-reporting.json') then Exit;   // never overwrite a choice made meanwhile
    if not DirExists(Dir) then ForceDirectories(Dir);
    if UsageBox.Checked[0] then SaveStringToFile(Dir + '\usage-reporting.json', '{"schema":"' + USAGE_SCHEMA + '","choice":"on","source":"installer"}', False)
    else SaveStringToFile(Dir + '\usage-reporting.json', '{"schema":"' + USAGE_SCHEMA + '","choice":"off","source":"installer"}', False);
  end;
end;

procedure InitializeWizard;
var Intro, Note: TNewStaticText; Top: Integer;
begin
  CheckGap := ScaleX(CHECK_GAP); ShowWordmark; LabelIndent := CheckLabelIndent;
  RoomyChecks(WizardForm.TasksList); RoomyChecks(WizardForm.RunList);   // Setup's own lists: the Shortcuts page and Start Classic Cart Studio now
  TermsFiles[0] := 'ccs-terms.txt'; TermsFiles[1] := 'ccs-license.txt'; TermsFiles[2] := 'ccs-notices.txt';
  TermsNames[0] := 'Classic Cart Studio Installation Terms 1.3.txt'; TermsNames[1] := 'GNU General Public License v3.txt'; TermsNames[2] := 'Classic Cart Studio Third-Party Notices.txt';
  ExtractTemporaryFile(TermsFiles[0]); ExtractTemporaryFile(TermsFiles[1]); ExtractTemporaryFile(TermsFiles[2]);
  TermsPage := CreateCustomPage(wpWelcome, 'Before You Install', 'Please read the terms, then check the box to agree.');
  Intro := TNewStaticText.Create(TermsPage);
  Intro.Parent := TermsPage.Surface; Intro.Left := 0; Intro.Top := 0; Intro.Width := TermsPage.SurfaceWidth; Intro.WordWrap := True; Intro.AutoSize := True;
  Intro.Caption := 'Classic Cart Studio is free, independent software. The terms below explain the risks of using it, of relying on backups and of changing your NES Classic.';
  Top := Intro.Top + Intro.Height + ScaleY(10);
  TermsShow := TNewComboBox.Create(TermsPage);
  TermsShow.Parent := TermsPage.Surface; TermsShow.Style := csDropDownList; TermsShow.Left := 0; TermsShow.Top := Top; TermsShow.Width := TermsPage.SurfaceWidth - ScaleX(120);
  TermsShow.Items.Add('Terms of Use'); TermsShow.Items.Add('Free Software License (GNU GPL)'); TermsShow.Items.Add('Credits for Other Software');
  TermsShow.ItemIndex := 0; TermsShow.OnChange := @TermsShowChange;
  TermsSave := TNewButton.Create(TermsPage);
  TermsSave.Parent := TermsPage.Surface; TermsSave.Caption := '&Save a Copy...'; TermsSave.Width := ScaleX(110); TermsSave.Height := TermsShow.Height + ScaleY(2);
  TermsSave.Left := TermsPage.SurfaceWidth - TermsSave.Width; TermsSave.Top := Top - ScaleY(1); TermsSave.OnClick := @TermsSaveClick;
  Top := Top + TermsShow.Height + ScaleY(8);
  Note := TNewStaticText.Create(TermsPage);   // under the agreement's own words (its left edge is theirs)
  Note.Parent := TermsPage.Surface; Note.Left := LabelIndent; Note.Width := TermsPage.SurfaceWidth - LabelIndent; Note.WordWrap := True; Note.AutoSize := True;
  Note.Caption := 'Your open-source rights and rights protected by law are preserved. Installing Classic Cart Studio does not, by itself, authorize console changes. Any console modification requires a separate review and your explicit approval.';
  TermsAgree := TNewCheckListBox.Create(TermsPage);
  TermsAgree.Parent := TermsPage.Surface; TermsAgree.Left := 0; TermsAgree.Width := TermsPage.SurfaceWidth; TermsAgree.Flat := True; TermsAgree.BorderStyle := bsNone;
  TermsAgree.ParentColor := True; RoomyChecks(TermsAgree);
  TermsAgree.WantTabs := True;   // checkbox-like (Inno's own pattern): no list background or selection bar on the dark style, Space toggles
  TermsAgree.AddCheckBox(AGREE_LABEL, '', 0, False, True, False, False, nil); FitCheckRow(TermsAgree, AGREE_LABEL); TermsAgree.OnClickCheck := @TermsAgreeClick;
  // from the bottom up, above the background's horizon line: the note (always visible), the agreement above it, the text filling what is left
  Note.Top := SafeBottom(TermsPage.Surface) - Note.Height;
  TermsAgree.Top := Note.Top - TermsAgree.Height - ScaleY(2);
  TermsText := TNewMemo.Create(TermsPage);
  TermsText.Parent := TermsPage.Surface; TermsText.Left := 0; TermsText.Top := Top; TermsText.Width := TermsPage.SurfaceWidth;
  TermsText.Height := TermsAgree.Top - Top - ScaleY(10); TermsText.ReadOnly := True; TermsText.WordWrap := True; TermsText.ScrollBars := ssVertical; TermsText.TabStop := True;
  TermsShowChange(nil);
  // keyboard order: the document choice, Save a Copy, the text, the agreement
  TermsShow.TabOrder := 0; TermsSave.TabOrder := 1; TermsText.TabOrder := 2; TermsAgree.TabOrder := 3;
  CreateUsagePage;
  RuntimePage := CreateOutputMarqueeProgressPage('Getting Ready', 'Making sure this computer has what Classic Cart Studio needs.');
  ClosePage := CreateOutputMarqueeProgressPage('Getting Ready', 'Closing Classic Cart Studio' + #$2026);
  ClosePage.SetText('Classic Cart Studio closes as it does when you close its window. If a game is open, answer its question.', '');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = TermsPage.ID then begin   // (Setup sets every other page's buttons itself)
    WizardForm.NextButton.Caption := '&Continue';
    WizardForm.NextButton.Enabled := TermsAgree.Checked[0] or WizardSilent;   // a silent setup goes on by itself (it stops at a disabled Next: "Failed to proceed")
    WizardForm.ActiveControl := TermsText;   // the page opens on the text itself (not a highlighted document choice)
  end;
  if CurPageID = wpSelectTasks then WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);   // the last page before installing (the Ready page is off): its button installs
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpPreparing) and WizardForm.PreparingYesRadio.Visible and WizardForm.PreparingYesRadio.Checked then begin
    Log('Getting Ready: Close it for me');
    if not CloseStudioForUpdate(True) then begin
      MsgBox(STILL_OPEN, mbInformation, MB_OK);
      Result := False;
    end;
    Exit;
  end;
  if (CurPageID = TermsPage.ID) and not TermsAgree.Checked[0] then begin
    if WizardSilent then Exit;   // silent setup: no page was shown and nothing is recorded as agreed (see above)
    MsgBox('To keep going, check the box that says you agree. You can also go Back or Cancel.', mbInformation, MB_OK);
    Result := False;
  end;
end;

// the .ccs default and content type are removed only when they are still Classic Cart Studio's (a choice the person made stays)
procedure RemoveOwnValue(const Name, Ours: String);
var V: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Classes\.ccs', Name, V) and (V = Ours) then RegDeleteValue(HKCU, 'Software\Classes\.ccs', Name);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var DataDir: String; ErrorCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnValue('', 'ClassicCartStudio.Collection');
    RemoveOwnValue('Content Type', 'application/vnd.classiccartstudio.collection+zip');
  end;
  // after the uninstaller removed its own values and keys: the .ccs keys, and the parents of the Open With registration under
  // Applications\{#AppExe} (its SupportedTypes and shell\open\command keys are removed above), go only when nothing else is left in
  // them, deepest first; a key that still holds anything (another program's or the person's own values) is kept as it is
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\.ccs\OpenWithProgids');
    RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\.ccs');
    RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\Applications\{#AppExe}\shell\open');
    RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\Applications\{#AppExe}\shell');
    RegDeleteKeyIfEmpty(HKCU, 'Software\Classes\Applications\{#AppExe}');
  end;
  // the data folder is kept on purpose: say where it is, and offer to open it (nothing is deleted or uploaded)
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    DataDir := ExpandConstant('{localappdata}') + '\ClassicCartStudio';
    if DirExists(DataDir) then
    begin
      if MsgBox('Classic Cart Studio was removed from this computer. Your collections, settings and games were not deleted.' + #13#10#13#10 +
                'They are still saved here:' + #13#10 + DataDir + #13#10#13#10 +
                'You can delete that folder yourself if you no longer need them. Open it now?', mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', DataDir, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end
    else
      MsgBox('Classic Cart Studio was removed from this computer.', mbInformation, MB_OK);
  end;
end;
