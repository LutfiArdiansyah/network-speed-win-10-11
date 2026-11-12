; Installer Network Speed (spec 8). Dikompilasi oleh tools\build-release.ps1; jangan jalankan ISCC langsung tanpa
; /DPublishDir. Teks ke pengguna dalam bahasa Inggris; komentar dalam bahasa Indonesia. Isi file ASCII saja.
;
; Parameter silent (didokumentasikan di docs\it-guide.md):
;   Setup : /VERYSILENT /SUPPRESSMSGBOXES /NORESTART [/UPDATEFOLDER="\\server\share\NetworkSpeed"] [/MERGETASKS="!startmenu"]
;   Uninstall (unins000.exe): /VERYSILENT /SUPPRESSMSGBOXES /NORESTART [/REMOVEDATA]

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must be defined (use tools\build-release.ps1)
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#define ServiceName "NetworkSpeedService"
#define AppExeName "NetworkSpeed.App.exe"
#define ServiceExeName "NetworkSpeed.Service.exe"

[Setup]
; AppId TIDAK BOLEH berubah: upgrade mengenali instalasi lama lewat ID ini.
AppId={{9C80639C-48FB-4546-AF26-A9C18385C5EC}
AppName=Network Speed
AppVersion={#AppVersion}
AppVerName=Network Speed {#AppVersion}
AppPublisher=KCT
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\NetworkSpeed
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 22H2 (19045) dan Windows 11 (spec C-01).
MinVersion=10.0.19045
OutputDir={#OutputDir}
OutputBaseFilename=NetworkSpeedSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
; App & Service masing-masing membawa runtime .NET (self-contained, spec 8): dictionary besar agar file runtime yang
; identik di kedua folder terkompresi sekali saja.
LZMADictionarySize=262144
; Dictionary sebesar ini tidak muat di proses compiler 32-bit (lzma error 5); kompresi di proses terpisah.
LZMAUseSeparateProcess=yes
WizardStyle=modern
SetupLogging=yes
; Penutupan App ditangani sendiri lewat sinyal keluar (spec 8), bukan Restart Manager.
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\app\{#AppExeName}
UninstallDisplayName=Network Speed
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"

[InstallDelete]
; Upgrade: buang file versi lama agar tidak ada DLL basi yang tertinggal.
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\service"

[Files]
Source: "{#PublishDir}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Network Speed"; Filename: "{app}\app\{#AppExeName}"; Tasks: startmenu

[Registry]
; Autostart untuk semua user (spec 6); user bisa mematikannya untuk dirinya di Settings.
Root: HKLM64; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NetworkSpeed"; ValueData: """{app}\app\{#AppExeName}"" --autostart"; Flags: uninsdeletevalue
; Pengaturan tingkat PC (spec 5): hanya ditulis bila parameter diberikan, agar upgrade mempertahankan nilai lama.
Root: HKLM64; Subkey: "Software\NetworkSpeed"; ValueType: string; ValueName: "UpdateFolder"; ValueData: "{param:UPDATEFOLDER}"; Check: HasUpdateFolderParam

[Run]
Filename: "{app}\app\{#AppExeName}"; Description: "Launch Network Speed"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
const
  EventModifyState = $0002;
  ExitEventName = 'Global\NetworkSpeed.AppExit';
  AppCloseWaitSteps = 30;      { x 500 ms = 15 detik }
  ServiceStopWaitSteps = 60;   { x 500 ms = 30 detik }

function OpenEvent(DesiredAccess: Cardinal; InheritHandle: BOOL; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function ResetEvent(Handle: THandle): BOOL;
  external 'ResetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function HasCommandLineSwitch(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

function HasUpdateFolderParam: Boolean;
begin
  Result := Trim(ExpandConstant('{param:UPDATEFOLDER}')) <> '';
end;

function RunHidden(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log(Format('Run: %s %s -> %d', [ExtractFileName(FileName), Params, Code]));
  Result := Code;
end;

function Sc(const Params: String): Integer;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), Params);
end;

{ WMI melihat proses di semua sesi (installer berjalan sebagai admin). Hanya untuk MEMPERCEPAT penantian:
  -1 = WMI gagal (sering rusak di PC kantor) dan pemanggil menganggapnya "masih berjalan", sehingga menunggu
  sampai batas waktu lalu memakai fallback tanpa syarat. Keputusan penting tidak bergantung pada WMI. }
function WmiCount(const Query: String): Integer;
var
  Locator, Service, Items: Variant;
begin
  Result := -1;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery(Query);
    Result := Items.Count;
  except
    Log('WMI query failed: ' + GetExceptionMessage);
  end;
end;

function IsAppRunning: Boolean;
begin
  Result := WmiCount('SELECT ProcessId FROM Win32_Process WHERE Name = ''{#AppExeName}''') <> 0;
end;

{ Exit code sc query: 1060 = service tidak ada. Tidak bergantung pada WMI. }
function ServiceExists: Boolean;
begin
  Result := Sc('query {#ServiceName}') <> 1060;
end;

function ServiceStopped: Boolean;
begin
  Result := WmiCount('SELECT Name FROM Win32_Service WHERE Name = ''{#ServiceName}'' AND State <> ''Stopped''') = 0;
end;

{ Spec 8: tutup App di semua sesi dengan sopan lewat event; taskkill bila masih berjalan setelah 15 detik.
  Sinyal dikirim tanpa syarat (tidak bergantung pada deteksi proses). }
procedure CloseRunningApps;
var
  Handle: THandle;
  I: Integer;
begin
  Handle := OpenEvent(EventModifyState, False, ExitEventName);
  if Handle <> 0 then
  begin
    SetEvent(Handle);
    Log('Exit signal sent to running apps.');
  end
  else
    Log('Exit signal not available (service not running?).');
  try
    for I := 1 to AppCloseWaitSteps do
    begin
      if not IsAppRunning then
        Break;
      Sleep(500);
    end;
  finally
    { Jangan biarkan event tetap signaled bila instalasi gagal: App yang start nanti akan langsung keluar. }
    if Handle <> 0 then
    begin
      ResetEvent(Handle);
      CloseHandle(Handle);
    end;
  end;
  if IsAppRunning then
  begin
    { Juga saat WMI gagal: taskkill tanpa proses yang cocok hanya mengembalikan 128. }
    Log('App still running after the exit signal (or state unknown); terminating.');
    RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}');
    Sleep(1000);
  end
  else
    Log('All app instances closed.');
end;

procedure StopServiceAndWait;
var
  I: Integer;
begin
  if not ServiceExists then
    Exit;
  Sc('stop {#ServiceName}');
  for I := 1 to ServiceStopWaitSteps do
  begin
    if ServiceStopped then
      Exit;
    Sleep(500);
  end;
  Log('Service did not stop in time; terminating.');
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#ServiceExeName}');
  Sleep(1000);
end;

{ Bandingkan versi "a.b.c[.d]"; bagian yang tidak ada = 0. }
function NextVersionPart(var S: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P = 0 then
  begin
    Result := StrToIntDef(S, 0);
    S := '';
  end
  else
  begin
    Result := StrToIntDef(Copy(S, 1, P - 1), 0);
    S := Copy(S, P + 1, Length(S));
  end;
end;

function CompareVersions(A, B: String): Integer;
var
  I, X, Y: Integer;
begin
  Result := 0;
  for I := 1 to 4 do
  begin
    X := NextVersionPart(A);
    Y := NextVersionPart(B);
    if X <> Y then
    begin
      if X > Y then Result := 1 else Result := -1;
      Exit;
    end;
  end;
end;

function InstalledVersion: String;
begin
  Result := '';
  RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9C80639C-48FB-4546-AF26-A9C18385C5EC}_is1', 'DisplayVersion', Result);
end;

function InitializeSetup: Boolean;
var
  Installed: String;
begin
  Result := True;
  Installed := InstalledVersion;
  if (Installed <> '') and (CompareVersions('{#AppVersion}', Installed) < 0) then
  begin
    Log(Format('Downgrade refused: installed %s, setup %s.', [Installed, '{#AppVersion}']));
    SuppressibleMsgBox(Format('Network Speed %s is already installed. Installing the older version %s is not supported.', [Installed, '{#AppVersion}']), mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  CloseRunningApps;
  StopServiceAndWait;
end;

procedure SetupDataFolder;
var
  Dir: String;
begin
  Dir := ExpandConstant('{commonappdata}\NetworkSpeed');
  ForceDirectories(Dir);
  { Spec H-05: folder bisa dibuat lebih dulu oleh user biasa beserta ACE eksplisit miliknya. Ambil alih owner,
    /reset membuang SEMUA ACE eksplisit (folder & isinya), lalu DACL diganti total: SYSTEM/Administrators (Full)
    dan LocalService (Modify); user biasa tanpa akses. }
  RunHidden(ExpandConstant('{sys}\icacls.exe'), '"' + Dir + '" /setowner *S-1-5-32-544 /T /C');
  RunHidden(ExpandConstant('{sys}\icacls.exe'), '"' + Dir + '" /reset /T /C');
  { Hanya pada FOLDER (tanpa /T): ACE (OI)(CI) diwariskan ke semua isi. Dengan /T, file kehilangan ACE warisan
    sementara grant (OI)(CI) tidak berlaku untuk file -> DACL file kosong -> service tidak bisa membuka history.db. }
  RunHidden(ExpandConstant('{sys}\icacls.exe'), '"' + Dir + '" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-19:(OI)(CI)M /C');
end;

procedure RegisterAndStartService;
var
  Args: String;
begin
  Args := ' binPath= "\"' + ExpandConstant('{app}\service\{#ServiceExeName}') + '\"" start= auto obj= "NT AUTHORITY\LocalService" DisplayName= "Network Speed"';
  if ServiceExists then
    Sc('config {#ServiceName}' + Args)
  else
    Sc('create {#ServiceName}' + Args);
  Sc('description {#ServiceName} "Records network usage history and per-app traffic for Network Speed."');
  Sc('failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000');
  if Sc('start {#ServiceName}') <> 0 then
    Log('Service did not start; it will be retried at the next boot.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SetupDataFolder;
    RegisterAndStartService;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApps;
    StopServiceAndWait;
    if ServiceExists then
      Sc('delete {#ServiceName}');
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\NetworkSpeed');
    { Data per user (%LOCALAPPDATA%) tidak pernah dihapus; data PC hanya dengan /REMOVEDATA (spec 8). }
    if HasCommandLineSwitch('/REMOVEDATA') then
    begin
      if DelTree(ExpandConstant('{commonappdata}\NetworkSpeed'), True, True, True) then
        Log('PC data removed.')
      else
        Log('PC data could not be removed completely.');
    end;
  end;
end;
