#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
[Setup]
AppId={{AEAD2361-3C54-45F4-BCE9-6D8D821D281C}
AppName=Cinevo Beta
AppVerName=Cinevo {#AppVersion} Beta
AppVersion={#AppVersion}
AppPublisher=BlackGen3ral
AppPublisherURL=https://github.com/BlackGen3ral/cinevo
DefaultDirName={localappdata}\Programs\Cinevo
DefaultGroupName=Cinevo
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Cinevo-Setup
SetupIconFile=..\Cinevo\Assets\cinevo.ico
UninstallDisplayIcon={app}\Cinevo.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=yes
SetupMutex=Cinevo.Setup
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: checkedonce
[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Cinevo"; Filename: "{app}\Cinevo.exe"
Name: "{autodesktop}\Cinevo"; Filename: "{app}\Cinevo.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Cinevo.exe"; Description: "Abrir o Cinevo"; Flags: nowait postinstall skipifsilent
