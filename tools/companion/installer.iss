; Inno Setup script — NES Classic Collection Studio Companion (per-user install, no administrator rights).
; Build: ISCC.exe /DAppVersion=0.1.0 /DSourceDir=..\..\build\dist\NESClassicCollectionStudioCompanion /DOutDir=..\..\build\release installer.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\dist\NESClassicCollectionStudioCompanion"
#endif
#ifndef OutDir
  #define OutDir "..\..\build\release"
#endif
#define AppName "NES Classic Collection Studio Companion"
#define AppExe "NESClassicCollectionStudioCompanion.exe"
#define AppPublisher "NES Classic Collection Studio project (independent; not affiliated with Nintendo)"
#define AppURL "https://rance9.github.io/NES-Classic-Collection-Studio/"
#define RepoURL "https://github.com/Rance9/NES-Classic-Collection-Studio"

[Setup]
AppId={{7E4C1C3A-6B0C-4B59-9F3D-2A1D5C0E9B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#RepoURL}/issues
AppUpdatesURL={#RepoURL}/releases
DefaultDirName={localappdata}\Programs\NESClassicCollectionStudio
DisableDirPage=yes
DefaultGroupName=NES Classic Collection Studio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutDir}
OutputBaseFilename=NESClassicCollectionStudio-Setup-{#AppVersion}
SetupIconFile={#SourceDir}\_internal\site\brand\public\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Connects the NES Classic Collection Studio website to this computer"
Name: "{group}\NES Classic Collection Studio (website)"; Filename: "{#AppURL}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; nesstudio:// link handler (per user). The website uses it only to bring the companion forward; the companion re-validates every origin.
Root: HKCU; Subkey: "Software\Classes\nesstudio"; ValueType: string; ValueName: ""; ValueData: "URL:NES Classic Collection Studio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\nesstudio\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start the companion now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; program files only — the user's data (%LOCALAPPDATA%\NESClassicCollectionStudio: imported games, personal games, collections, logs) is kept on purpose
Type: filesandordirs; Name: "{app}\_internal"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('The companion was removed. Your imported games, personal games and collections were kept in ' + ExpandConstant('{localappdata}') + '\NESClassicCollectionStudio. Delete that folder yourself if you no longer want them.', mbInformation, MB_OK);
end;
