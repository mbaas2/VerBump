#Requires -Version 5
<#
.SYNOPSIS
    VerBump Release Script
    Baut die App, kompiliert den Installer und erstellt einen GitHub Release.

.PARAMETER Notes
    Optionale Release Notes (werden dem gh-Befehl übergeben).
    Ohne Angabe öffnet GitHub den Editor.
#>
param(
    [string]$Notes = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root    = $PSScriptRoot
$SrcDir  = Join-Path $Root "src"
$Version = (Get-Content (Join-Path $SrcDir "VERSION") -Raw).Trim()
$Iscc    = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$OutDir  = "C:\devt\VerBump\installer"

Write-Host ""
Write-Host "=== VerBump Release v$Version ===" -ForegroundColor Cyan
Write-Host ""

# ── 1. Publish ────────────────────────────────────────────────────────────────
Write-Host "[1/5] dotnet publish..." -ForegroundColor Yellow
dotnet clean "$SrcDir\VerBump.csproj" -c Release --nologo -v quiet
dotnet publish "$SrcDir\VerBump.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none `
    --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen." }
Write-Host "      OK" -ForegroundColor Green

# ── 2. Installer ──────────────────────────────────────────────────────────────
Write-Host "[2/5] Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $Iscc)) { throw "ISCC.exe nicht gefunden: $Iscc" }
& $Iscc /Q "$SrcDir\VerBump.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen." }
$Installer = Join-Path $OutDir "VerBump-Setup-$Version.exe"
if (-not (Test-Path $Installer)) { throw "Installer nicht gefunden: $Installer" }
Write-Host "      OK  →  $Installer" -ForegroundColor Green

# ── 2.5 Staleness-Check via VerBump selbst ────────────────────────────────────
$PublishExe = "$SrcDir\bin\Release\net8.0-windows\win-x64\publish\VerBump.exe"
if (Test-Path $PublishExe) {
    Write-Host "[2.5] VERSION-Check (VerBump --check)..." -ForegroundColor Yellow
    & $PublishExe $SrcDir --check | Out-Null
    if ($LASTEXITCODE -eq 1) {
        Write-Host "      VERSION ist veraltet! VerBump wird geöffnet..." -ForegroundColor Red
        & $PublishExe $SrcDir
        $ans = Read-Host "      Release trotzdem fortsetzen? (J/N)"
        if ($ans -notmatch '^[Jj]') { throw "Release abgebrochen." }
    } else {
        Write-Host "      VERSION ist aktuell." -ForegroundColor Green
    }
}

# ── 3. Git commit + push ──────────────────────────────────────────────────────
Write-Host "[3/5] Git commit..." -ForegroundColor Yellow
Set-Location $Root
git add -A
git status --short
git commit -m "Release v$Version"
if ($LASTEXITCODE -ne 0) { throw "git commit fehlgeschlagen." }
Write-Host "      OK" -ForegroundColor Green

Write-Host "[4/5] Git push..." -ForegroundColor Yellow
git push
if ($LASTEXITCODE -ne 0) { throw "git push fehlgeschlagen." }
Write-Host "      OK" -ForegroundColor Green

# ── 4.5 Letzte Chance: Release Notes prüfen + bestätigen ─────────────────────
$NextNotes    = Join-Path $Root "release-notes-next.md"
Write-Host ""
Write-Host "─── Release Notes (release-notes-next.md) ───────────────────────" -ForegroundColor DarkGray
if (Test-Path $NextNotes) {
    Get-Content $NextNotes | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "  (keine release-notes-next.md gefunden — GitHub generiert Notes automatisch)" -ForegroundColor DarkYellow
}
Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
$confirm = Read-Host "Release v$Version jetzt auf GitHub veröffentlichen? (J/N)"
if ($confirm -notmatch '^[Jj]') { throw "Release abgebrochen — kein GitHub-Release erstellt." }

# ── 5. GitHub Release ─────────────────────────────────────────────────────────
Write-Host "[5/5] GitHub Release v$Version..." -ForegroundColor Yellow
$VersionNotes = Join-Path $Root "release-notes-v$Version.md"

# release-notes-next.md → release-notes-v{VERSION}.md  (vNEXT ersetzen)
if (Test-Path $NextNotes) {
    (Get-Content $NextNotes -Raw) -replace 'vNEXT', "v$Version" | Set-Content $VersionNotes -NoNewline
    Remove-Item $NextNotes
    Write-Host "      release-notes-next.md → release-notes-v$Version.md" -ForegroundColor DarkGray
}

$ghArgs = @(
    "release", "create", "v$Version",
    $Installer,
    "--title", "VerBump v$Version"
)
if ($Notes -ne "") {
    $ghArgs += @("--notes", $Notes)
} elseif (Test-Path $VersionNotes) {
    $ghArgs += @("--notes-file", $VersionNotes)
} else {
    $ghArgs += "--generate-notes"
}
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release fehlgeschlagen." }

# Leere release-notes-next.md für den nächsten Release anlegen
Set-Content $NextNotes "# What's new in vNEXT`n`n## New features`n`n`n`n## Improvements`n`n`n`n---`n`n## Was ist neu in vNEXT`n`n## Neue Features`n`n`n`n## Verbesserungen`n`n"
Write-Host "      Neue release-notes-next.md angelegt." -ForegroundColor DarkGray

Write-Host ""
Write-Host "=== Release v$Version fertig! ===" -ForegroundColor Green
Write-Host "    https://github.com/mbaas2/VerBump/releases/tag/v$Version"
Write-Host ""
