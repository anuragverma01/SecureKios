#define MyAppName "SecureKiosk"
#define MyAppVersion "1.0.0.0"
#define MyAppPublisher "SecureKiosk"

[Setup]
AppId={{D37E834B-6A10-4A3C-9B62-81D23E4571A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputBaseFilename=SecureKiosk_Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\..\artifacts\SecureKiosk.App_1.0.0.0_x64.msix"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\..\artifacts\SecureKiosk-Dev.cer"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1. Silently import certificate into LocalMachine\TrustedPeople so Windows natively trusts the package
Filename: "certutil.exe"; Parameters: "-addstore -f ""TrustedPeople"" ""{tmp}\SecureKiosk-Dev.cer"""; StatusMsg: "Configuring system security..."; Flags: runhidden waituntilterminated

; 2. Silently install the MSIX package and launch it
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxPackage -Path '{tmp}\SecureKiosk.App_1.0.0.0_x64.msix'; $p = Get-AppxPackage -Name SecureKiosk; if ($p) { Start-Process (\"shell:AppsFolder\\\" + $p.PackageFamilyName + \"!App\") }"""; StatusMsg: "Installing SecureKiosk application..."; Flags: runhidden waituntilterminated
