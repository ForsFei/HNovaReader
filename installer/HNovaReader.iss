; Compile with Inno Setup 6.3 or newer via tools/Build-Installer.ps1.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to a self-contained win-x64 publish directory.
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir SourcePath + "..\artifacts\installer"
#endif

[Setup]
AppId={{CC0CF468-9478-44B9-A1B0-01693C1183B1}
AppName=HNovaReader
AppVersion={#AppVersion}
AppPublisher=ForsFei
AppPublisherURL=https://github.com/ForsFei/HNovaReader
DefaultDirName={localappdata}\Programs\HNovaReader
DefaultGroupName=HNovaReader
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutputDir}
OutputBaseFilename=HNovaReader-{#AppVersion}-win-x64-Setup
SetupIconFile=..\HNovaReader\Assets\HNovaReader.ico
UninstallDisplayIcon={app}\HNovaReader.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
; No UninstallDelete entries: never remove the library or user data.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HNovaReader"; Filename: "{app}\HNovaReader.exe"
Name: "{autodesktop}\HNovaReader"; Filename: "{app}\HNovaReader.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HNovaReader.exe"; Description: "Launch HNovaReader"; Flags: nowait postinstall skipifsilent
