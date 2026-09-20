; WeighBridge Modern - Universal Production Smart Installer Script
; Inno Setup 6.x configuration with automated OS version detection
; Full deployment across Windows 7 SP1, 8, 8.1, 10, 11 (32-bit and 64-bit)

#define MyAppName "WeighBridge Modern"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WeighBridge Systems"
#define MyAppExeName "WeighBridge.App.exe"
#define DiagExeName "WeighBridge.SerialDiagnostic.exe"
#define TerminalExeName "Terminal.exe"
#define ModernSourceDir "..\publish\modern-win-x64"
#define LegacySourceDir "..\publish\legacy-win-x86"
#define AssetsDir "..\installer-assets"

[Setup]
AppId={{D982D428-F7A1-4D90-A875-1029487123A1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\publish\installer
OutputBaseFilename=WeighBridge_Modern_Setup_v{#MyAppVersion}
SetupIconFile={#AssetsDir}\Icons\truck.ico
UninstallDisplayIcon={app}\truck.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Modern 64-bit binaries (Installed on Windows 10, 11, 8.1, 7 SP1 64-bit)
Source: "{#ModernSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: Is64BitInstallMode
; Legacy 32-bit binaries (Installed on 32-bit Windows systems)
Source: "{#LegacySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not Is64BitInstallMode

; Application icon
Source: "{#AssetsDir}\Icons\truck.ico"; DestDir: "{app}"; Flags: ignoreversion

; Driver staging (Temporary during setup execution)
Source: "{#AssetsDir}\Drivers\*"; DestDir: "{tmp}\Drivers"; Flags: deleteafterinstall recursesubdirs

; VC++ Redistributables staging (Temporary during setup execution)
Source: "{#AssetsDir}\Redist\*"; DestDir: "{tmp}\Redist"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\truck.ico"
Name: "{group}\{#MyAppName} (Safe Graphics Mode)"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--software-render"; IconFilename: "{app}\truck.ico"
Name: "{group}\Hardware Serial Diagnostic Tool"; Filename: "{app}\{#DiagExeName}"; IconFilename: "{app}\truck.ico"
Name: "{group}\Legacy Terminal Utility"; Filename: "{app}\Tools\{#TerminalExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\truck.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; Configure TLS 1.2 on Windows 7 SChannel for SMS and Cloud API connectivity
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client"; ValueType: dword; ValueName: "DisabledByDefault"; ValueData: 0; Flags: createvalueifdoesntexist noerror; Check: IsLegacyWindows
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client"; ValueType: dword; ValueName: "Enabled"; ValueData: 1; Flags: createvalueifdoesntexist noerror; Check: IsLegacyWindows

[Code]
// Win32 API declarations for dynamic kernel inspection
function GetModuleHandle(lpModuleName: String): THandle;
  external 'GetModuleHandleW@kernel32.dll stdcall';
function GetProcAddress(hModule: THandle; lpProcName: AnsiString): LongWord;
  external 'GetProcAddress@kernel32.dll stdcall';

// System version detection
function IsWindows10OrNewer(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major >= 10);
end;

function IsLegacyWindows(): Boolean;
begin
  Result := not IsWindows10OrNewer();
end;

// Verify Windows 7 SetDefaultDllDirectories (KB2533623 / KB3063858)
function CheckWin7Prerequisites(): Boolean;
var
  Version: TWindowsVersion;
  hKernel: THandle;
  pFunc: LongWord;
begin
  GetWindowsVersionEx(Version);
  Result := True;
  
  // Only check on Windows 7 (Major=6, Minor=1)
  if (Version.Major = 6) and (Version.Minor = 1) then
  begin
    hKernel := GetModuleHandle('kernel32.dll');
    if hKernel <> 0 then
    begin
      pFunc := GetProcAddress(hKernel, 'SetDefaultDllDirectories');
      if pFunc = 0 then
      begin
        Log('Warning: SetDefaultDllDirectories not found in kernel32.dll (KB2533623/KB3063858 is missing).');
        MsgBox(
          'Notice: This Windows 7 system may be missing security update KB2533623 or KB3063858.' + #13#10 + #13#10 +
          'If the application fails to start, please ensure update KB3063858 for Windows 7 is installed.',
          mbInformation, MB_OK);
      end
      else
      begin
        Log('Verified: kernel32.dll has SetDefaultDllDirectories.');
      end;
    end;
  end;
end;


// Verify and install Microsoft Visual C++ 2015-2022 Redistributable
procedure InstallVCRedist();
var
  RegKey: String;
  IsInstalled: Cardinal;
  RedistExe: String;
  Params: String;
  ResultCode: Integer;
begin
  IsInstalled := 0;
  
  if Is64BitInstallMode() then
  begin
    RegKey := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64';
    if RegQueryDWordValue(HKLM, RegKey, 'Installed', IsInstalled) and (IsInstalled = 1) then
    begin
      Log('Visual C++ 2015-2022 x64 Redistributable is already installed.');
      Exit;
    end;
    RedistExe := ExpandConstant('{tmp}\Redist\vc_redist.x64.exe');
  end
  else
  begin
    RegKey := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X86';
    if RegQueryDWordValue(HKLM, RegKey, 'Installed', IsInstalled) and (IsInstalled = 1) then
    begin
      Log('Visual C++ 2015-2022 x86 Redistributable is already installed.');
      Exit;
    end;
    RedistExe := ExpandConstant('{tmp}\Redist\vc_redist.x86.exe');
  end;

  if FileExists(RedistExe) then
  begin
    Log('Installing Visual C++ Redistributable: ' + RedistExe);
    Params := '/quiet /norestart';
    if Exec(RedistExe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log('Visual C++ Redistributable installed successfully (Exit code: ' + IntToStr(ResultCode) + ')')
    else
      Log('Failed to execute Visual C++ Redistributable (Error code: ' + IntToStr(ResultCode) + ')');
  end
  else
  begin
    Log('Visual C++ Redistributable package not found at: ' + RedistExe);
  end;
end;

// Install MA112 Hardware Driver silently using certutil and pnputil
procedure InstallMA112Driver();
var
  CertPath: String;
  DriverInfPath: String;
  Params: String;
  ResultCode: Integer;
  Version: TWindowsVersion;
  PnpUtilPath: String;
begin
  GetWindowsVersionEx(Version);
  PnpUtilPath := ExpandConstant('{sys}\pnputil.exe');
  
  // 1. Pre-trust Megawin signing certificate so Windows does not show security prompt
  CertPath := ExpandConstant('{tmp}\Drivers\MA112\megawin.cer');
  if FileExists(CertPath) then
  begin
    Log('Adding Megawin certificate to TrustedPublisher store: ' + CertPath);
    Params := '-addstore -f "TrustedPublisher" "' + CertPath + '"';
    if Exec(ExpandConstant('{sys}\certutil.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log('certutil executed successfully (Exit code: ' + IntToStr(ResultCode) + ')')
    else
      Log('certutil execution failed.');
  end;

  // 2. Install / Stage driver using pnputil
  if (Version.Major >= 10) then
  begin
    // Windows 10 / 11 syntax
    DriverInfPath := ExpandConstant('{tmp}\Drivers\MA112\SHA-2\0E6A0122_MA112 USB to UART Data Bridge_v1.00.inf');
    if FileExists(DriverInfPath) then
    begin
      Log('Installing modern SHA-2 driver via pnputil: ' + DriverInfPath);
      Params := '/add-driver "' + DriverInfPath + '" /install';
      Exec(PnpUtilPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Log('pnputil finished with exit code: ' + IntToStr(ResultCode));
    end;
  end
  else
  begin
    // Windows 7 / 8 / 8.1 syntax
    // Stage SHA-1 driver (for factory Win 7 without KB4474419)
    DriverInfPath := ExpandConstant('{tmp}\Drivers\MA112\SHA-1\0E6A0122_MA112 USB to UART Data Bridge_v1.00.inf');
    if FileExists(DriverInfPath) then
    begin
      Log('Staging SHA-1 driver via pnputil: ' + DriverInfPath);
      Params := '-i -a "' + DriverInfPath + '"';
      Exec(PnpUtilPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Log('pnputil SHA-1 finished with exit code: ' + IntToStr(ResultCode));
    end;

    // Stage SHA-2 driver (if KB4474419 is installed)
    DriverInfPath := ExpandConstant('{tmp}\Drivers\MA112\SHA-2\0E6A0122_MA112 USB to UART Data Bridge_v1.00.inf');
    if FileExists(DriverInfPath) then
    begin
      Log('Staging SHA-2 driver via pnputil: ' + DriverInfPath);
      Params := '-i -a "' + DriverInfPath + '"';
      Exec(PnpUtilPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Log('pnputil SHA-2 finished with exit code: ' + IntToStr(ResultCode));
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  
  // Verify Windows 7 Service Pack 1 minimum
  if (Version.Major < 6) or ((Version.Major = 6) and (Version.Minor = 1) and (Version.ServicePackMajor < 1)) then
  begin
    MsgBox('This application requires Windows 7 Service Pack 1 or newer.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  CheckWin7Prerequisites();
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 1. Install VC++ Redistributable (UCRT / MSVCP140) if missing
    InstallVCRedist();
    
    // 2. Install hardware driver for Megawin MA112
    InstallMA112Driver();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\WeighBridge Modern');
    if DirExists(DataDir) then
    begin
      if MsgBox('Do you want to retain your historical weighment database and tickets?' + #13#10 + #13#10 +
                'Click "Yes" to keep your data safe, or "No" to delete all records.', mbConfirmation, MB_YESNO) = IDNO then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
