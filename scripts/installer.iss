; WeighBridge Modern - Production Smart Installer Script
; Inno Setup 6.x configuration with automated OS version detection
; Deploys Modern (Win 10/11) or Legacy (Win 7/8) runtime packages automatically.

#define MyAppName "WeighBridge Modern"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WeighBridge Systems"
#define MyAppExeName "WeighBridge.App.exe"
#define ModernSourceDir "..\publish\modern-win-x64"
#define LegacySourceDir "..\publish\legacy-win-x86"

[Setup]
AppId={{D982D428-F7A1-4D90-A875-1029487123A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\publish\installer
OutputBaseFilename=WeighBridge_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Modern 64-bit files (Installed on Windows 10, Windows 11, and modern Windows Server)
Source: "{#ModernSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsModernWindowsAnd64Bit
; Legacy files (Installed on Windows 7 SP1, Windows 8, Windows 8.1, or 32-bit systems)
Source: "{#LegacySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsLegacyWindowsOr32Bit

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Detects Windows 10 or newer (Version >= 10.0)
function IsWindows10OrNewer(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major >= 10);
end;

// Condition for Modern 64-bit deployment
function IsModernWindowsAnd64Bit(): Boolean;
begin
  Result := IsWindows10OrNewer() and Is64BitInstallMode();
end;

// Condition for Legacy or 32-bit deployment
function IsLegacyWindowsOr32Bit(): Boolean;
begin
  Result := not IsModernWindowsAnd64Bit();
end;

// Environment check on installation startup
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  
  // Verify Windows 7 SP1 minimum requirement
  if (Version.Major < 6) or ((Version.Major = 6) and (Version.Minor = 1) and (Version.ServicePackMajor < 1)) then
  begin
    MsgBox('This application requires Windows 7 Service Pack 1 or newer.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  if IsModernWindowsAnd64Bit() then
    Log('Installer detected Modern 64-bit Windows environment. Deploying high-performance .NET 8 package.')
  else
    Log('Installer detected Legacy Windows environment. Deploying compatibility package.');

  Result := True;
end;
