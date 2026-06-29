#define AppName "Screen Subtitle Translator"
#define AppVersion "0.1.0"
#define AppPublisher "Screen Subtitle Translator"
#define AppExeName "ScreenSubtitleTranslator.exe"
#define PublishDir "..\artifacts\release\ScreenSubtitleTranslator-v0.1.0-win-x64"

[Setup]
AppId={{DD15DB6B-C0B8-4BE6-B626-971798407B61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/ConnorsDL/ScreenSubtitleTranslator
AppSupportURL=https://github.com/ConnorsDL/ScreenSubtitleTranslator/issues
AppUpdatesURL=https://github.com/ConnorsDL/ScreenSubtitleTranslator/releases
DefaultDirName={autopf}\Screen Subtitle Translator
DefaultGroupName=Screen Subtitle Translator
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ScreenSubtitleTranslatorSetup-v0.1.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
UninstallDisplayName={#AppName} v{#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=0.1.0.0
VersionInfoProductVersion=0.1.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "start.bat,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Screen Subtitle Translator"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Screen Subtitle Translator"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
