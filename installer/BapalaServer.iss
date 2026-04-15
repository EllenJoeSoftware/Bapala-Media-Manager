; ─────────────────────────────────────────────────────────────────────────────
; BapalaServer Inno Setup script
;
; Prerequisites:
;   1. Inno Setup 6.3+  →  https://jrsoftware.org/isinfo.php
;   2. Run the publish command first (see README-BUILD.md) so the
;      ..\publish\win-x64\ folder exists.
;
; Build this installer with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" BapalaServer.iss
;
; Output:  installer\Output\BapalaServer-Setup-1.0.0.exe
; ─────────────────────────────────────────────────────────────────────────────

#define AppName      "Bapala Media Server"
#define AppVersion   "1.0.0"
#define AppPublisher "EllenJoe Software"
#define AppURL       "https://ellenjoesoftware.co.za"
#define ServiceName  "BapalaMediaServer"
#define ExeName      "BapalaServer.exe"
#define PublishDir   "..\publish\win-x64"

[Setup]
AppId={{A3F7C2B1-8E44-4D9A-B6F0-2C1D5E8A9B3C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\EllenJoe Software\Bapala Media Server
DefaultGroupName=Bapala Media Server
AllowNoIcons=yes
LicenseFile=
OutputDir=Output
OutputBaseFilename=BapalaServer-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
; Minimum Windows 10 (required for .NET 10 and mDNS)
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; (WizardImageFile/WizardSmallImageFile omitted — uses Inno Setup defaults)

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startservice"; Description: "Start Bapala Media Server immediately after install"; GroupDescription: "Service options:"
Name: "firewall";     Description: "Add Windows Firewall exception for port 8484";         GroupDescription: "Network:"

[Files]
; All published files (self-contained, no .NET runtime required on target)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Bapala Media Server";         Filename: "{app}\{#ExeName}"
Name: "{group}\Open Web Interface";          Filename: "{app}\open-browser.bat"
Name: "{group}\Uninstall Bapala Media Server"; Filename: "{uninstallexe}"

[Run]
; Stop + remove any previous installation of the service
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#ServiceName}"""; Flags: runhidden waituntilterminated; Check: ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#ServiceName}"""; Flags: runhidden waituntilterminated; Check: ServiceExists

; Register as a Windows Service (auto-start)
Filename: "{sys}\sc.exe"; Parameters: "create ""{#ServiceName}"" binPath= ""{app}\{#ExeName}"" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated

; Set service description
Filename: "{sys}\sc.exe"; Parameters: "description ""{#ServiceName}"" ""Bapala home media server — streams video to the Bapala app on your LAN."""; Flags: runhidden waituntilterminated

; Optionally start the service immediately
Filename: "{sys}\sc.exe"; Parameters: "start ""{#ServiceName}"""; Flags: runhidden waituntilterminated; Tasks: startservice

; Open the web interface in the default browser after install
Filename: "{app}\open-browser.bat"; Description: "Open Bapala web interface"; Flags: postinstall skipifsilent shellexec

[UninstallRun]
; Stop and remove the service on uninstall
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#ServiceName}""";   Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#ServiceName}"""; Flags: runhidden waituntilterminated

[Code]
// ── Helper: check if the Windows service already exists ──────────────────────
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), ExpandConstant('query "{#ServiceName}"'),
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

// ── Add / remove Windows Firewall rule ───────────────────────────────────────
procedure AddFirewallRule();
var
  ResultCode: Integer;
begin
  Exec('netsh', 'advfirewall firewall add rule name="Bapala Media Server" ' +
       'dir=in action=allow protocol=TCP localport=8484',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RemoveFirewallRule();
var
  ResultCode: Integer;
begin
  Exec('netsh', 'advfirewall firewall delete rule name="Bapala Media Server"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('firewall') then
      AddFirewallRule();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFirewallRule();
end;
