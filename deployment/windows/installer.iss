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
Source: "install-app.cmd"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
Filename: "{tmp}\install-app.cmd"; StatusMsg: "Installing SecureKiosk application..."; Flags: runhidden waituntilterminated
