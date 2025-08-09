; AudioPopFix Tray – Inno Setup Script (single-file publish, user-mode install)

; If the build passes /DMyAppVersion=..., use it; otherwise fallback.
#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#define MyAppName "AudioPopFix Tray"
#define MyAppPublisher "You"
#define MyAppExeName "AudioPopFixTray.exe"
#define PubExePath "..\publish\AudioPopFixTray.exe"

[Setup]
AppId={{B6A9C5F0-2EAA-4C69-9B46-6B8B9F6B2A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\AudioPopFix
DisableProgramGroupPage=no
OutputDir=.
OutputBaseFilename=AudioPopFixTray_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=AudioPopFix_app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start AudioPopFix Tray with Windows"; GroupDescription: "Additional tasks:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Files]
; Single-file publish output (self-contained EXE)
Source: "{#PubExePath}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; User desktop (avoids admin requirement)
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[Registry]
; Optional "Start with Windows" (per-user Run key)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "AudioPopFixTray"; \
  ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup

