#ifndef AppVersion
  #define AppVersion "0.1.38"
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
Filename: "{app}\Cinevo.exe"; Parameters: "--after-update"; Flags: nowait runasoriginaluser; Check: CinevoUpdate
Filename: "{app}\Cinevo.exe"; Description: "Abrir o Cinevo"; Flags: nowait postinstall skipifsilent

[Code]
function OpenProcess(Access: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessId: Integer;
  ProcessHandle: THandle;
begin
  Result := '';
  ProcessId := StrToIntDef(ExpandConstant('{param:CinevoWaitPid|0}'), 0);
  if ProcessId <= 0 then Exit;
  ProcessHandle := OpenProcess($00100000, False, ProcessId);
  if ProcessHandle = 0 then Exit;
  try
    if WaitForSingleObject(ProcessHandle, 30000) <> 0 then
      Result := 'O Cinevo ainda está encerrando. Feche o app e tente a atualização novamente.';
  finally
    CloseHandle(ProcessHandle);
  end;
end;

function CinevoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:CinevoUpdate|0}') = '1';
end;
