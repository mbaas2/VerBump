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

; ── Dateien ────────────────────────────────────────────────────────────────────

[Files]
; Self-contained single-file exe (bringt .NET-Runtime mit, keine Installation erforderlich)
Source: "{#PublishDir}\{#AppExeName}";  DestDir: "{app}"; Flags: ignoreversion

; Sprachdateien
Source: "{#PublishDir}\lang.de.json";   DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\lang.en.json";   DestDir: "{app}"; Flags: ignoreversion

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
