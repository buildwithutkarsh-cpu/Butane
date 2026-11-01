#ifndef PublishDir
  #error PublishDir must point to the published application files.
#endif

#ifndef OutputDir
  #define OutputDir "dist"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B97C61BE-17BD-4BF0-9DA8-3E8F23D1238A}
AppName=Butane
AppVersion={#AppVersion}
AppPublisher=Butane
DefaultDirName={localappdata}\Programs\Butane
DefaultGroupName=Butane
UninstallDisplayIcon={app}\Butane.exe
UninstallDisplayName=Butane
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=Butane-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Butane"; Filename: "{app}\Butane.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Butane"; Filename: "{app}\Butane.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Butane.exe"; Description: "Launch Butane"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent
