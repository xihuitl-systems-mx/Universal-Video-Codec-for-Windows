#ifndef BundlePath
  #define BundlePath "..\artifacts\native-v2"
#endif
#define ProductName "Universal Video Codec for Windows"

[Setup]
AppId={{069338A8-D42A-4B08-A6DE-E00669F561D5}
AppName={#ProductName}
AppVersion=2.0.0
AppVerName={#ProductName} 2.0.0
DefaultDirName={autopf}\{#ProductName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
WizardStyle=modern
OutputDir=..\dist
OutputBaseFilename=Universal Video Codec for Windows Setup
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\x64\HevcCodec.Cli.exe

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#BundlePath}\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,ffmpeg.exe"
Source: "{#BundlePath}\x86\*"; DestDir: "{app}\x86"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,ffmpeg.exe"
Source: "..\native\runtime\dotnet-runtime-10.0.12-win-x64.exe"; Flags: dontcopy
Source: "..\native\runtime\dotnet-runtime-10.0.12-win-x86.exe"; Flags: dontcopy
Source: "..\..\Aceso\vendor\ffmpeg\windows\ffmpeg.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\Aceso\vendor\ffmpeg\windows\LICENSE"; DestDir: "{app}"; DestName: "FFMPEG-LICENSE.txt"; Flags: ignoreversion
Source: "..\native\LGPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\native\GPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\HevcCodec.Cli.exe"
Type: files; Name: "{app}\HevcCodec.Cli.dll"
Type: files; Name: "{app}\HevcCodec.Cli.deps.json"
Type: files; Name: "{app}\HevcCodec.Cli.runtimeconfig.json"
Type: files; Name: "{app}\HevcCodec.dll"
Type: files; Name: "{app}\HevcCodec.comhost.dll"
Type: files; Name: "{app}\HevcCodec.deps.json"
Type: files; Name: "{app}\HevcCodec.runtimeconfig.json"
Type: files; Name: "{app}\libde265.dll"
Type: files; Name: "{app}\LAVFilters-0.83-Installer.exe"

[UninstallRun]
Filename: "{app}\x86\HevcCodec.Cli.exe"; Parameters: "unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterX86"
Filename: "{app}\x64\HevcCodec.Cli.exe"; Parameters: "unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterX64"

[Code]
var
  RegistrationFailed: Boolean;

function RuntimeInstalled(Arch: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + Arch + '\sharedfx\Microsoft.NETCore.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('10.0.', Names[I]) = 1 then Result := True;
  if not Result then
    if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\' + Arch + '\sharedfx\Microsoft.NETCore.App', Names) then
      for I := 0 to GetArrayLength(Names) - 1 do
        if Pos('10.0.', Names[I]) = 1 then Result := True;
end;

function InstallRuntime(Arch: String; var NeedsRestart: Boolean): String;
var
  FileName: String;
  ExitCode: Integer;
begin
  Result := '';
  if RuntimeInstalled(Arch) then Exit;
  FileName := 'dotnet-runtime-10.0.12-win-' + Arch + '.exe';
  ExtractTemporaryFile(FileName);
  if not Exec(ExpandConstant('{tmp}\') + FileName, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'No se pudo instalar una dependencia del códec.'
  else if (ExitCode <> 0) and (ExitCode <> 3010) and (ExitCode <> 1641) then
    Result := 'La instalación de una dependencia falló. Código: ' + IntToStr(ExitCode)
  else if (ExitCode = 3010) or (ExitCode = 1641) then NeedsRestart := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := InstallRuntime('x64', NeedsRestart);
  if Result = '' then Result := InstallRuntime('x86', NeedsRestart);
end;

procedure RunRegistrar(Arch, Command: String);
var
  ExitCode: Integer;
begin
  Log('Codec ' + Arch + ': ' + Command);
  if not Exec(ExpandConstant('{app}\') + Arch + '\HevcCodec.Cli.exe', Command, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('No se pudo registrar el códec para ' + Arch + '.');
  if ExitCode <> 0 then RaiseException('No se pudo registrar el códec para ' + Arch + '. Código: ' + IntToStr(ExitCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegistrationFailed := True;
    RunRegistrar('x64', 'cleanupold');
    RunRegistrar('x86', 'register');
    RunRegistrar('x64', 'register');
    RunRegistrar('x86', 'enumtest');
    RunRegistrar('x64', 'enumtest');
    RegistrationFailed := False;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if RegistrationFailed then Result := 1 else Result := 0;
end;
