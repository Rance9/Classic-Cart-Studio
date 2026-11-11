; Inno Setup script — Classic Cart Studio for Windows (per-user install, no administrator rights).
; Build: ISCC.exe /DAppVersion=0.10.0 /DSourceDir=..\..\build\dist\ClassicCartStudioBackend /DOutDir=..\..\build\release installer.iss
; The frozen backend tree (SourceDir) also carries ClassicCartStudio.exe and the WebView2 assemblies, copied there by the release workflow. The wizard's
; pictures (tools/publish/make_installer_art.py) and the setup's icon come from the repository, relative to this script, like the legal files.
; Needs Inno Setup 6.7.0 or later (the dark style's hidebevels, WizardBackColor and WizardBackImageFile).
; Stable technical identifiers are kept on purpose (upgrade compatibility with the earlier "companion" releases): AppId, the install
; folder, the nesstudio:// link type; the program folder, the backend executable and the data folder carry the product name since 0.3.2 (data moved on first start).
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\dist\ClassicCartStudioBackend"
#endif
#ifndef OutDir
  #define OutDir "..\..\build\release"
#endif
#define AppName "Classic Cart Studio"
#define AppExe "ClassicCartStudio.exe"
#define BackendExe "ClassicCartStudioBackend.exe"
#define AppPublisher "Classic Cart Studio Project - Rance9"
#define AppURL "https://classiccartstudio.com/"
#define RepoURL "https://github.com/Rance9/Classic-Cart-Studio"
#if Ver < EncodeVer(6, 7, 0)
  #error Inno Setup 6.7.0 or later is required (WizardStyle dark with hidebevels, WizardBackColor, WizardBackImageFile)
#endif

[Setup]
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
; per-user only (owner decision): always the non-administrative install mode, with no install-mode dialog and no /ALLUSERS override
; (PrivilegesRequiredOverridesAllowed is left at its default, none); program files, shortcuts and registrations are all per user
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=ClassicCartStudio-Setup-{#AppVersion}
SetupIconFile=..\publish\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
; the owner-approved look (2026-11-10): dark, the title bar included, no bevel lines; the Welcome and Finished pages' picture, the CCS mark at
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
WelcomeLabel1=Welcome to Classic Cart Studio
WelcomeLabel2=This installs [name/ver] for your Windows account. No administrator rights are needed.%n%nYour ROM files are never uploaded, and your collections and backups stay on this computer.
SelectTasksLabel2=Select the additional tasks you would like Setup to perform while installing [name], then click Install.
FinishedHeadingLabel=Classic Cart Studio is ready
FinishedLabel=[name] is installed. Start it now, or later from the Start menu.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; FIRST, the small files the pages need before anything is installed (extracted for them): with solid compression a file is reached only
; after everything before it is unpacked, so placed after the program they held the first page back for about 20 s.
; the wordmark at every display scale: the page header loads the one drawn for this display's scale (ShowWordmark)
Source: "..\publish\assets\installer\wizard-wordmark-*.png"; Flags: dontcopy
; the Installation Terms (tools/publish/legal_terms.py writes this copy from the one maintained source) and the licenses: shown by the
; Licenses and Important Risks page (extracted for it, nothing installed yet) and installed beside the program, readable offline
Source: "..\publish\assets\legal\INSTALLATION-TERMS.txt"; DestName: "ccs-terms.txt"; Flags: dontcopy
Source: "..\..\LICENSE"; DestName: "ccs-license.txt"; Flags: dontcopy
Source: "..\..\THIRD_PARTY_NOTICES.md"; DestName: "ccs-notices.txt"; Flags: dontcopy
; what the optional usage reports collect (the setup's "What is collected?" and a copy beside the program)
Source: "..\publish\assets\legal\USAGE-REPORTS.txt"; DestName: "ccs-usage.txt"; Flags: dontcopy
Source: "..\publish\assets\legal\INSTALLATION-TERMS.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}\legal"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\legal"; DestName: "THIRD_PARTY_NOTICES.txt"; Flags: ignoreversion
Source: "..\publish\assets\legal\USAGE-REPORTS.txt"; DestDir: "{app}\legal"; Flags: ignoreversion
; the program
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; one Start menu entry, no folder and no website shortcut (owner, 2026-10-18)
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\ClassicCartStudio.ico"; Comment: "Build and prepare your NES Classic game collection"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; nesstudio:// link handler (per user), kept for compatibility. The website uses it only to open or bring forward the application
; (nesstudio://open, nesstudio://handoff?offer=<id>); the application accepts an offer id only, never commands, paths or data.
Root: HKCU; Subkey: "Software\Classes\nesstudio"; ValueType: string; ValueName: ""; ValueData: "URL:Classic Cart Studio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Flags: uninsdeletekey
; classiccartstudio:// links (per user): the website opens the application on a published collection (classiccartstudio://share/<id>), on
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

[InstallDelete]
; the Start menu folder of earlier versions (app shortcut + website shortcut): replaced by the single entry above
Type: files; Name: "{userprograms}\Classic Cart Studio\Classic Cart Studio.lnk"
Type: files; Name: "{userprograms}\Classic Cart Studio\Classic Cart Studio website.url"
Type: dirifempty; Name: "{userprograms}\Classic Cart Studio"
; the bundled program files are replaced as a whole (0.5.0: the interface, library and artwork live here; files an earlier version
; carried must not linger). User data is not in {app}: it stays in %LOCALAPPDATA%\ClassicCartStudio.
Type: filesandordirs; Name: "{app}\_internal"
; shortcuts of the earlier "NES Classic Collection Studio Companion" releases (0.1.x): same AppId, new names and group
Type: files; Name: "{userprograms}\NES Classic Collection Studio\NES Classic Collection Studio Companion.lnk"
Type: files; Name: "{userprograms}\NES Classic Collection Studio\NES Classic Collection Studio (website).url"
Type: files; Name: "{userprograms}\NES Classic Collection Studio\Classic Cart Studio.lnk"
Type: files; Name: "{userprograms}\NES Classic Collection Studio\Classic Cart Studio website.url"
Type: dirifempty; Name: "{userprograms}\NES Classic Collection Studio"
Type: files; Name: "{autodesktop}\NES Classic Collection Studio Companion.lnk"
Type: filesandordirs; Name: "{localappdata}\Programs\NESClassicCollectionStudio"

[UninstallDelete]
; program files only — the user's data (%LOCALAPPDATA%\ClassicCartStudio: imported games, personal games, collections, logs) is kept on purpose
Type: filesandordirs; Name: "{app}\_internal"

[Code]
// ---- Licenses and Important Risks: after Welcome, before anything is installed. The full Installation Terms, the GNU GPL and the
// third-party notices are readable here (from the setup itself, offline) and can be saved; the agreement box starts unchecked and
// Continue stays unavailable until it is checked. Nothing is recorded or sent: no receipt is kept. A silent setup (/SILENT, /VERYSILENT)
// shows no pages, so no one agrees there: it installs without recording any agreement, and the same terms are installed in legal\
// ---- Optional usage and error reports: a separate page after the terms, its own choice (never part of the agreement). The box starts
// UNCHECKED on every new installation: nothing the setup can read reliably establishes where it runs or which opt-in rule applies
// (Windows' Country or region and time zone are settings a person can change, and an EU visitor on a US-set laptop looks the same), so the
// agreed fallback for unknown applicability applies (D-124, Codex K3F-F3): only the person turns it on. No network or location lookup.
// (k3f6 started it checked for a US region with a US time zone; removed in k3f7.) A choice already kept on this computer (usage-reporting.json in the app's data folder: an earlier setup or the
// Studio menu's Usage Reporting) is kept as it is: the page is skipped and nothing is written, so an upgrade, repair or reinstall never
// changes it, and an opt-out stays; any usage-reporting.json already there, even one the app would not accept, is never overwritten (no
// choice there means off, and the Studio menu can set it). The choice is written once the program files are in place, before the app can
// first start; Cancel writes nothing; a silent setup shows no page and writes nothing (no choice means off). The setup itself never sends anything.
// The data folder: while an earlier release's NESClassicCollectionStudio folder exists and ClassicCartStudio does not, the app moves the
// whole earlier folder to the new name on its first start (App.cs DataRoot and common.py _data_root, both only when the new folder is
// absent). The setup must not create the new folder then, or that move would be skipped and the person's data would seem lost: the
// choice is written INTO the earlier folder and moves with it.
const
  USAGE_SCHEMA = 'ccs-usage-choice/1';
  OBM_CHECKBOXES = 32759; CHECK_OFFSET = 4;   // TNewCheckListBox: its check width is a quarter of Windows' check-box bitmap, with 4 px either side
  WORDMARK_W = 92; WORDMARK_H = 28;   // the wordmark's size at 100 % (tools/publish/make_installer_art.py draws it at every scale)
  USAGE_LABEL = 'Help improve Classic Cart Studio with basic usage and error reports (recommended)';
  AGREE_LABEL = 'I agree to the Installation Terms and acknowledge the important risks. My GPL and non-waivable legal rights remain unchanged.';
var
  TermsPage: TWizardPage; TermsShow: TNewComboBox; TermsText: TNewMemo; TermsAgree: TNewCheckListBox; TermsSave: TNewButton;
  TermsFiles: array[0..2] of String; TermsNames: array[0..2] of String;
  UsagePage: TWizardPage; UsageBox: TNewCheckListBox; UsageStored: String;   // UsageStored: 'on' / 'off' kept on this computer, 'file' another file there, '' none
  LabelIndent: Integer;   // where a check box's own words start at this display's scale: text that belongs to a check box lines up with them

type
  TWinBitmap = record bmType, bmWidth, bmHeight, bmWidthBytes: Longint; bmPlanes, bmBitsPixel: Word; bmBits: Longint; end;

function LoadBitmapW(Inst: Longint; Name: Longint): Longint; external 'LoadBitmapW@user32.dll stdcall';
function GetObjectW(Obj: Longint; Size: Integer; var Bm: TWinBitmap): Integer; external 'GetObjectW@gdi32.dll stdcall';
function DeleteObject(Obj: Longint): Boolean; external 'DeleteObject@gdi32.dll stdcall';

function CheckLabelIndent: Integer;   // as TNewCheckListBox places a check box's words: its check width + 4 + 4 + 1 (22 px at 100 %, 34 at 200 %)
var H: Longint; Bm: TWinBitmap;
begin
  Result := ScaleX(17);   // (only if Windows cannot say: the 200 % value)
  H := LoadBitmapW(0, OBM_CHECKBOXES);
  if H <> 0 then begin
    if GetObjectW(H, 24, Bm) > 0 then Result := Bm.bmWidth div 4 + 2 * CHECK_OFFSET + 1;
    DeleteObject(H);
  end;
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

function UsageDataDir: String;   // the app's data folder; an earlier release's folder that the app moves there on its first start, if that one is still waiting
var Now_, Old: String;
begin
  Now_ := ExpandConstant('{localappdata}\ClassicCartStudio'); Old := ExpandConstant('{localappdata}\NESClassicCollectionStudio');
  if (not DirExists(Now_)) and DirExists(Old) then Result := Old else Result := Now_;
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
  H := WrappedHeight(L.Parent, S, L.ClientWidth - LabelIndent - CHECK_OFFSET);   // the list's own text width (its MeasureItem)
  L.MinItemHeight := H + ScaleY(4); L.Height := H + ScaleY(8);
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

procedure UsageDetailsClick(Sender: TObject);   // "What is collected?": the bundled text, offline
var F: TSetupForm; M: TNewMemo; B: TNewButton;
begin
  F := CreateCustomForm(ScaleX(560), ScaleY(440), False, True);
  try
    F.Caption := 'What is collected?';
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
  UsagePage := CreateCustomPage(TermsPage.ID, 'Optional Usage and Error Reports', 'A separate choice, not part of the agreement. All features work either way.');
  UsageBox := TNewCheckListBox.Create(UsagePage);
  UsageBox.Parent := UsagePage.Surface; UsageBox.Left := 0; UsageBox.Top := 0; UsageBox.Width := UsagePage.SurfaceWidth; UsageBox.Height := ScaleY(22);
  UsageBox.Flat := True; UsageBox.BorderStyle := bsNone; UsageBox.ParentColor := True;
  UsageBox.WantTabs := True;   // a plain check box on the page: no list background or selection bar on the dark style (as the terms' agreement)
  UsageBox.AddCheckBox(USAGE_LABEL, '', 0, False, True, False, False, nil);   // starts unchecked: only the person turns it on (see above) FitCheckRow(UsageBox, USAGE_LABEL);
  Desc := TNewStaticText.Create(UsagePage);
  Desc.Parent := UsagePage.Surface; Desc.Left := LabelIndent; Desc.Top := UsageBox.Top + UsageBox.Height + ScaleY(6); Desc.Width := UsagePage.SurfaceWidth - LabelIndent; Desc.WordWrap := True; Desc.AutoSize := True;
  Desc.Caption := 'These reports help us understand which features are useful and spot common problems, so we can prioritize fixes and make CCS more reliable. They never include your ROMs, game names, collections, saves or personal file details. You can turn reporting off now or anytime in the Studio menu, under Usage Reporting. All features work either way.';
  More := TNewButton.Create(UsagePage);
  More.Parent := UsagePage.Surface; More.Caption := '&What is collected?'; More.Width := ScaleX(150); More.Height := ScaleY(26);
  More.Left := LabelIndent; More.Top := Desc.Top + Desc.Height + ScaleY(14); More.OnClick := @UsageDetailsClick;
  UsageBox.TabOrder := 0; More.TabOrder := 1;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = UsagePage.ID) and (UsageStored <> '');   // a choice already kept here stays as it is
end;

procedure CurStepChanged(CurStep: TSetupStep);   // after the program files, before the app can first start ([Run] comes later)
var Dir: String;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) and (UsageStored = '') then begin
    Dir := UsageDataDir;   // decided again now: the earlier folder while it waits to be moved (it exists, so nothing new is created)
    if FileExists(Dir + '\usage-reporting.json') then Exit;   // never overwrite a choice made meanwhile
    if not DirExists(Dir) then ForceDirectories(Dir);   // only the new folder, and only when no earlier folder waits
    if UsageBox.Checked[0] then SaveStringToFile(Dir + '\usage-reporting.json', '{"schema":"' + USAGE_SCHEMA + '","choice":"on","source":"installer"}', False)
    else SaveStringToFile(Dir + '\usage-reporting.json', '{"schema":"' + USAGE_SCHEMA + '","choice":"off","source":"installer"}', False);
  end;
end;

procedure InitializeWizard;
var Intro, Note: TNewStaticText; Top: Integer;
begin
  ShowWordmark; LabelIndent := CheckLabelIndent;
  TermsFiles[0] := 'ccs-terms.txt'; TermsFiles[1] := 'ccs-license.txt'; TermsFiles[2] := 'ccs-notices.txt';
  TermsNames[0] := 'Classic Cart Studio Installation Terms 1.0.txt'; TermsNames[1] := 'GNU General Public License v3.txt'; TermsNames[2] := 'Classic Cart Studio Third-Party Notices.txt';
  ExtractTemporaryFile(TermsFiles[0]); ExtractTemporaryFile(TermsFiles[1]); ExtractTemporaryFile(TermsFiles[2]);
  TermsPage := CreateCustomPage(wpWelcome, 'Licenses and Important Risks', 'Please read these before continuing.');
  Intro := TNewStaticText.Create(TermsPage);
  Intro.Parent := TermsPage.Surface; Intro.Left := 0; Intro.Top := 0; Intro.Width := TermsPage.SurfaceWidth; Intro.WordWrap := True; Intro.AutoSize := True;
  Intro.Caption := 'Classic Cart Studio is independent, open-source software. Please review its licenses and the important software, backup and console risks before continuing. Your GPL and non-waivable legal rights remain unchanged.';
  Top := Intro.Top + Intro.Height + ScaleY(10);
  TermsShow := TNewComboBox.Create(TermsPage);
  TermsShow.Parent := TermsPage.Surface; TermsShow.Style := csDropDownList; TermsShow.Left := 0; TermsShow.Top := Top; TermsShow.Width := TermsPage.SurfaceWidth - ScaleX(120);
  TermsShow.Items.Add('Installation Terms (EULA)'); TermsShow.Items.Add('GNU General Public License v3'); TermsShow.Items.Add('Third-party notices');
  TermsShow.ItemIndex := 0; TermsShow.OnChange := @TermsShowChange;
  TermsSave := TNewButton.Create(TermsPage);
  TermsSave.Parent := TermsPage.Surface; TermsSave.Caption := '&Save a Copy...'; TermsSave.Width := ScaleX(110); TermsSave.Height := TermsShow.Height + ScaleY(2);
  TermsSave.Left := TermsPage.SurfaceWidth - TermsSave.Width; TermsSave.Top := Top - ScaleY(1); TermsSave.OnClick := @TermsSaveClick;
  Top := Top + TermsShow.Height + ScaleY(8);
  Note := TNewStaticText.Create(TermsPage);   // under the agreement's own words (its left edge is theirs)
  Note.Parent := TermsPage.Surface; Note.Left := LabelIndent; Note.Width := TermsPage.SurfaceWidth - LabelIndent; Note.WordWrap := True; Note.AutoSize := True;
  Note.Caption := 'This does not restrict your open-source rights or authorize console changes, advertising tracking or purchases.';
  TermsAgree := TNewCheckListBox.Create(TermsPage);
  TermsAgree.Parent := TermsPage.Surface; TermsAgree.Left := 0; TermsAgree.Width := TermsPage.SurfaceWidth; TermsAgree.Flat := True; TermsAgree.BorderStyle := bsNone;
  TermsAgree.ParentColor := True;
  TermsAgree.WantTabs := True;   // checkbox-like (Inno's own pattern): no list background or selection bar on the dark style, Space toggles
  TermsAgree.AddCheckBox(AGREE_LABEL, '', 0, False, True, False, False, nil); FitCheckRow(TermsAgree, AGREE_LABEL); TermsAgree.OnClickCheck := @TermsAgreeClick;
  // from the bottom up: the note (always visible), the agreement above it, the text filling what is left
  Note.Top := TermsPage.SurfaceHeight - Note.Height;
  TermsAgree.Top := Note.Top - TermsAgree.Height - ScaleY(2);
  TermsText := TNewMemo.Create(TermsPage);
  TermsText.Parent := TermsPage.Surface; TermsText.Left := 0; TermsText.Top := Top; TermsText.Width := TermsPage.SurfaceWidth;
  TermsText.Height := TermsAgree.Top - Top - ScaleY(10); TermsText.ReadOnly := True; TermsText.WordWrap := True; TermsText.ScrollBars := ssVertical; TermsText.TabStop := True;
  TermsShowChange(nil);
  // keyboard order: the document choice, Save a Copy, the text, the agreement
  TermsShow.TabOrder := 0; TermsSave.TabOrder := 1; TermsText.TabOrder := 2; TermsAgree.TabOrder := 3;
  CreateUsagePage;
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
  if (CurPageID = TermsPage.ID) and not TermsAgree.Checked[0] then begin
    if WizardSilent then Exit;   // silent setup: no page was shown and nothing is recorded as agreed (see above)
    MsgBox('To continue, check the box to agree to the Installation Terms. You can also go Back, or Cancel the setup.', mbInformation, MB_OK);
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
      if MsgBox('Classic Cart Studio was removed from this computer. Your collections, settings and imported ROM files were not deleted.' + #13#10#13#10 +
                'They are still saved here:' + #13#10 + DataDir + #13#10#13#10 +
                'Delete that folder yourself if you no longer need them. Open it now?', mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', DataDir, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end
    else
      MsgBox('Classic Cart Studio was removed from this computer.', mbInformation, MB_OK);
  end;
end;
