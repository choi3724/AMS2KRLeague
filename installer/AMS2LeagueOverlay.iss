#ifndef SourceDir
  #error SourceDir must be provided by build-release.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by build-release.ps1
#endif
#ifndef AppVersion
  #define AppVersion "0.2"
#endif

[Setup]
AppId={{4C7E39B4-F9B4-44CA-A99D-C7029662EFA7}
AppName=AMS2 League Overlay
AppVersion={#AppVersion}
AppPublisher=AMS2 Korea League
AppPublisherURL=https://krams2.mycafe24.com/ams2/
AppSupportURL=https://github.com/choi3724/AMS2KRLeague/issues
DefaultDirName={localappdata}\Programs\AMS2 League Overlay
DefaultGroupName=AMS2 League Overlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=AMS2-League-Overlay-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\AMS2LeagueClient.exe
CloseApplications=force
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AMS2 League Overlay"; Filename: "{app}\AMS2LeagueClient.exe"; WorkingDir: "{app}"
Name: "{group}\제거"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\AMS2LeagueClient.exe"; Description: "AMS2 League Overlay 실행"; Flags: nowait postinstall skipifsilent
