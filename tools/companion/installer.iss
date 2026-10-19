; Inno Setup script — Classic Cart Studio for Windows (per-user install, no administrator rights).
; Build: ISCC.exe /DAppVersion=0.6.1 /DSourceDir=..\..\build\dist\ClassicCartStudioBackend /DOutDir=..\..\build\release installer.iss
; The frozen backend tree (SourceDir) also carries ClassicCartStudio.exe, the WebView2 assemblies and the wizard images, copied there by the release workflow.
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
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutDir}
OutputBaseFilename=ClassicCartStudio-Setup-{#AppVersion}
SetupIconFile={#SourceDir}\_internal\site\brand\public\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
WizardImageFile={#SourceDir}\_internal\site\brand\public\wizard-large.bmp,{#SourceDir}\_internal\site\brand\public\wizard-large-200.bmp
WizardSmallImageFile={#SourceDir}\_internal\site\brand\public\wizard-small.bmp,{#SourceDir}\_internal\site\brand\public\wizard-small-200.bmp
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
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}

[Messages]
WelcomeLabel1=Welcome to Classic Cart Studio
WelcomeLabel2=This installs [name/ver] for your user account: the Studio window, its local backend and the Start menu shortcuts. No administrator rights are needed.%n%nYour game files and collections are never uploaded anywhere.
FinishedHeadingLabel=Ready to play
FinishedLabel=[name] is installed. Start it from the Start menu, or right now: it opens the Studio website in its own window.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
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
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    MsgBox('Classic Cart Studio was removed. Your imported games, personal games and collections were kept in ' + ExpandConstant('{localappdata}') + '\ClassicCartStudio. Delete that folder yourself if you no longer want them.', mbInformation, MB_OK);
end;
