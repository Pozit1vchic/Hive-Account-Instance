#define MyAppName "Hive"
#ifndef SourceDir
  #error SourceDir must point to the staged release directory
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
[Setup]
AppId={{77C4C70C-2AF8-44EC-82EB-7FDFDEAA1071}
AppName={#MyAppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Hive
DefaultGroupName=Hive
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts\release
OutputBaseFilename=Hive-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Hive.App.exe
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Hive"; Filename: "{app}\Hive.App.exe"
Name: "{autodesktop}\Hive"; Filename: "{app}\Hive.App.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Run]
Filename: "{app}\Hive.App.exe"; Description: "Open Hive"; Flags: nowait postinstall skipifsilent
