# What's new in vNEXT

## New features

- **Explorer context menu** — right-click any folder to open it in VerBump directly;
  right-click a `VERSION` file for silent one-click bumping (Major / Minor / Patch);
  right-click any `settings.json` to open it directly in VerBump.
  All three options are available as optional entries during installation.

- **Git pre-commit hook** — install or remove a hook directly from the Settings dialog
  (per project). When installed, VerBump blocks the commit if `VERSION` is stale and
  opens automatically so you can bump before anything goes out.
  Bypass for a single commit with `git commit --no-verify`.

- **Load settings** — new toolbar button to switch to a different `settings.json` on the
  fly, with a most-recently-used list for quick switching between profiles.

- **Update check** — VerBump silently checks GitHub for a newer release on startup and
  shows a small toast notification with a download link. No telemetry, no blocking.

- **Command-line arguments**
  - `VerBump.exe <path>` — open VerBump with a specific `VERSION` file or folder pre-selected
  - `VerBump.exe --settings=<path>` — load settings from a custom location
  - `VerBump.exe <path> --bump=1|2|3` — silently bump Major / Minor / Patch
    (used by the Explorer context menu; designed for SemVer)
  - `VerBump.exe <path> --check` — exit with code 1 if `VERSION` is stale,
    0 if current (used by the git pre-commit hook)

## Improvements

- **Ctrl+I** — show a dialog listing files newer than `VERSION` for the selected project
- Staleness indicator: the coloured bar on the left widens to 8 px when `VERSION` is stale;
  hovering over it shows the list of newer files
- About dialog redesigned with links to website, GitHub Sponsors, issue tracker,
  source code and a mail link
- Toolbar button renamed from "Info" to "About" / "Über"
- Settings dialog reorganised: global ignore rules first, then project list,
  then per-project details

---

## Was ist neu in vNEXT

## Neue Features

- **Explorer-Kontextmenü** — Rechtsklick auf einen Ordner öffnet VerBump direkt;
  Rechtsklick auf eine `VERSION`-Datei ermöglicht stilles Erhöhen (Major / Minor / Patch);
  Rechtsklick auf eine beliebige `settings.json` öffnet sie direkt in VerBump.
  Alle drei Optionen sind während der Installation optional wählbar.

- **Git pre-commit Hook** — Hook direkt im Einstellungs-Dialog installieren oder
  entfernen (pro Projekt). Wenn aktiv, blockiert VerBump den Commit bei veralteter
  `VERSION` und öffnet sich automatisch.
  Für einzelne Commits umgehbar mit `git commit --no-verify`.

- **Einstellungen öffnen** — neuer Toolbar-Button zum Wechseln der `settings.json`,
  mit Liste der zuletzt verwendeten Dateien.

- **Update-Check** — VerBump prüft beim Start still im Hintergrund, ob eine neue Version
  auf GitHub verfügbar ist, und zeigt einen kurzen Toast mit Download-Link.
  Keine Telemetrie, kein Blockieren des Starts.

- **Kommandozeilenargumente**
  - `VerBump.exe <Pfad>` — Projekt direkt vorauswählen
  - `VerBump.exe --settings=<Pfad>` — alternative Settings-Datei laden
  - `VerBump.exe <Pfad> --bump=1|2|3` — Version lautlos erhöhen (für SemVer)
  - `VerBump.exe <Pfad> --check` — Exit-Code 1 bei veralteter VERSION, sonst 0

## Verbesserungen

- **Strg+I** — zeigt eine Liste der neueren Dateien für das ausgewählte Projekt
- Staleness-Indikator: der farbige Balken links wird bei veralteter `VERSION` auf 8 px
  verbreitert; Hover zeigt die Liste der neueren Dateien
- About-Dialog neu gestaltet mit Links zu Website, GitHub Sponsors, Issue-Tracker,
  Quellcode und Mail
- Toolbar-Button von „Info" in „Über" umbenannt
- Einstellungs-Dialog neu strukturiert: globale Ignore-Regeln zuerst, dann
  Projektliste, dann Projekt-Details
