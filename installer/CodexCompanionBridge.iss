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
DefaultGroupName=Codex Companion
PrivilegesRequired=lowest
OutputDir=..\.artifacts
OutputBaseFilename=CodexCompanion-Bridge-Setup-win-x64-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "autostart"; Description: "登录 Windows 后自动启动 Bridge"; Flags: unchecked

[Icons]
Name: "{group}\启动 Codex Companion Bridge"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bridge-control.ps1"" -Action Start"; WorkingDir: "{app}"
Name: "{group}\停止 Codex Companion Bridge"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bridge-control.ps1"" -Action Stop"; WorkingDir: "{app}"
Name: "{group}\Bridge 状态"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bridge-control.ps1"" -Action Status"; WorkingDir: "{app}"
Name: "{group}\Bridge 配置"; Filename: "{app}\CodexCompanion.Bridge.exe"; Parameters: "setup"; WorkingDir: "{app}"
Name: "{group}\Bridge 诊断"; Filename: "{app}\CodexCompanion.Bridge.exe"; Parameters: "doctor"; WorkingDir: "{app}"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-bridge.ps1"""; Description: "配置 Codex Companion Bridge"; Flags: postinstall waituntilterminated skipifsilent
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bridge-control.ps1"" -Action EnableAutostart"; Description: "启用 Bridge 登录自启动"; Flags: postinstall waituntilterminated skipifsilent unchecked; Tasks: autostart
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bridge-control.ps1"" -Action Start"; Description: "安装完成后启动 Bridge"; Flags: postinstall waituntilterminated skipifsilent unchecked

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-bridge.ps1"""; Flags: runhidden waituntilterminated
