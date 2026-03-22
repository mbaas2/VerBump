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

# ── 5. GitHub Release ─────────────────────────────────────────────────────────
Write-Host "[5/5] GitHub Release v$Version..." -ForegroundColor Yellow
$NotesFile = Join-Path $Root "release-notes-v$Version.md"
$ghArgs = @(
    "release", "create", "v$Version",
    $Installer,
    "--title", "VerBump v$Version",
    "--tag",   "v$Version"
)
if ($Notes -ne "") {
    $ghArgs += @("--notes", $Notes)
} elseif (Test-Path $NotesFile) {
    $ghArgs += @("--notes-file", $NotesFile)
} else {
    $ghArgs += "--generate-notes"
}
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release fehlgeschlagen." }

Write-Host ""
Write-Host "=== Release v$Version fertig! ===" -ForegroundColor Green
Write-Host "    https://github.com/mbaas2/VerBump/releases/tag/v$Version"
Write-Host ""
