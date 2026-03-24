#define SourceDir    "C:\\Git\\VerBump\\src"
#define PublishDir   SourceDir + "\\bin\\Release\\net8.0-windows\\win-x64\\publish"
#define AppName      "VerBump"
#define AppVersion   Trim(FileRead(FileOpen(SourceDir + "\\VERSION")))
#define AppPublisher "Michael Baas - http://mbaas.de"
#define AppExeName   "VerBump.exe"

; ── Setup ──────────────────────────────────────────────────────────────────────

[Setup]
AppId={{6F3A2B1C-4D7E-4F9A-8C2D-1E5B3A7F9D4E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=C:\devt\VerBump\installer
OutputBaseFilename=VerBump-Setup-{#AppVersion}
SetupIconFile=verbump.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}

; ── Sprachen ───────────────────────────────────────────────────────────────────

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

; ── Optionale Aufgaben ─────────────────────────────────────────────────────────

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "contextmenu"; Description: "Explorer-Kontextmenü: Ordner mit VerBump öffnen"; GroupDescription: "Shell-Integration:"; Flags: unchecked
Name: "versionmenu";  Description: "Explorer-Kontextmenü: VERSION-Datei in VerBump öffnen und/oder direkt erhöhen"; GroupDescription: "Shell-Integration:"; Flags: unchecked
Name: "settingsmenu"; Description: "Explorer-Kontextmenü: verbump-settings.json direkt in VerBump öffnen";                              GroupDescription: "Shell-Integration:"; Flags: unchecked

; ── Registry (Kontextmenü) ─────────────────────────────────────────────────────

[Registry]
; Kontextmenü für Ordner (Rechtsklick auf Ordner)
Root: HKLM; Subkey: "Software\Classes\Directory\shell\VerBump";                    ValueType: string; ValueName: "";     ValueData: "Mit VerBump öffnen";              Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\shell\VerBump";                    ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExeName},0";           Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\shell\VerBump\command";            ValueType: string; ValueName: "";     ValueData: """{app}\{#AppExeName}"" ""%1""";  Tasks: contextmenu
; Kontextmenü für VERSION-Dateien: "In VerBump öffnen"
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Open";           ValueType: string; ValueName: "";          ValueData: "In VerBump öffnen";           Flags: uninsdeletekey; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Open";           ValueType: string; ValueName: "Icon";      ValueData: "{app}\{#AppExeName},0";       Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Open";           ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileName: ""VERSION"""; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Open\command";   ValueType: string; ValueName: "";          ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: versionmenu
; Kontextmenü für VERSION-Dateien: Submenu "Version erhöhen" (nested shell keys, zuverlässiger als CommandStore)
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION";                          ValueType: string; ValueName: "";            ValueData: "Version erhöhen (SemVer)";     Flags: uninsdeletekey; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION";                          ValueType: string; ValueName: "MUIVerb";     ValueData: "Version erhöhen (SemVer)";     Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION";                          ValueType: string; ValueName: "Icon";        ValueData: "{app}\{#AppExeName},0";        Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION";                          ValueType: string; ValueName: "SubCommands"; ValueData: "";                             Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION";                          ValueType: string; ValueName: "AppliesTo";   ValueData: "System.FileName: ""VERSION"""; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump1";              ValueType: string; ValueName: "";            ValueData: "Major +";                      Flags: uninsdeletekey; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump1\command";      ValueType: string; ValueName: "";            ValueData: """{app}\{#AppExeName}"" ""%1"" --bump=1"; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump2";              ValueType: string; ValueName: "";            ValueData: "Minor +";                      Flags: uninsdeletekey; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump2\command";      ValueType: string; ValueName: "";            ValueData: """{app}\{#AppExeName}"" ""%1"" --bump=2"; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump3";              ValueType: string; ValueName: "";            ValueData: "Patch +";                      Flags: uninsdeletekey; Tasks: versionmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.VERSION\shell\Bump3\command";      ValueType: string; ValueName: "";            ValueData: """{app}\{#AppExeName}"" ""%1"" --bump=3"; Tasks: versionmenu
; Kontextmenü für settings.json (Rechtsklick auf beliebige settings.json)
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Settings";           ValueType: string; ValueName: "";          ValueData: "In VerBump öffnen";                                   Flags: uninsdeletekey; Tasks: settingsmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Settings";           ValueType: string; ValueName: "Icon";      ValueData: "{app}\{#AppExeName},0";                               Tasks: settingsmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Settings";           ValueType: string; ValueName: "AppliesTo"; ValueData: "System.FileName: ""verbump-settings.json""";                 Tasks: settingsmenu
Root: HKLM; Subkey: "Software\Classes\*\shell\VerBump.Settings\command";   ValueType: string; ValueName: "";          ValueData: """{app}\{#AppExeName}"" ""%1""";                      Tasks: settingsmenu
; Kontextmenü für Ordner-Hintergrund (Rechtsklick im Ordner)
Root: HKLM; Subkey: "Software\Classes\Directory\Background\shell\VerBump";         ValueType: string; ValueName: "";     ValueData: "Mit VerBump öffnen";              Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\Background\shell\VerBump";         ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExeName},0";           Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\Background\shell\VerBump\command"; ValueType: string; ValueName: "";     ValueData: """{app}\{#AppExeName}"" ""%V""";  Tasks: contextmenu

; ── Dateien ────────────────────────────────────────────────────────────────────

[Files]
; Self-contained single-file exe (bringt .NET-Runtime mit, keine Installation erforderlich)
Source: "{#PublishDir}\{#AppExeName}";  DestDir: "{app}"; Flags: ignoreversion

; Sprachdateien
Source: "{#SourceDir}\lang.de.json";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\lang.en.json";    DestDir: "{app}"; Flags: ignoreversion

; VerBump-settings.json wird beim ersten Start automatisch in %APPDATA%\VerBump\ erstellt

; ── Verknüpfungen ──────────────────────────────────────────────────────────────

[Icons]
Name: "{group}\{#AppName}";                       Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";               Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; ── Nach der Installation starten ─────────────────────────────────────────────

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent
