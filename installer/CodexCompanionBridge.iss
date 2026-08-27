#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\\.artifacts\\bridge-win-x64-dev"
#endif

[Setup]
AppId={{E6D0B3D7-EC2A-4F1B-9F00-4C7A7BF2B0C6}
AppName=Codex Companion Bridge
AppVersion={#Version}
DefaultDirName={localappdata}\CodexCompanion\Bridge
PrivilegesRequired=lowest
OutputDir=..\.artifacts
OutputBaseFilename=CodexCompanion-Bridge-Setup-win-x64-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-bridge.ps1"""; Description: "配置并启动 Codex Companion Bridge"; Flags: postinstall waituntilterminated skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-bridge.ps1"""; Flags: runhidden waituntilterminated
