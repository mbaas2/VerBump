# What's new in vNEXT

## Improvements

- fixed a few localization issues

## CLI: `--help` flag

- `VerBump.exe --help` (also `-h` / `/?`) prints usage information and exits —
  useful when running VerBump from a terminal or CI script.

## Settings: lists reference window

- A `?` button next to the Lists field opens a non-modal reference window that
  documents list syntax, range expansion (`prefix{1-3}` → `prefix1, prefix2, prefix3`,
  `{01-09}` for zero-padded ranges), and usage in format strings — without
  leaving the settings dialog.

## Settings: validation on save

- Before saving, VerBump now checks each project entry and warns if the
  configured directory does not exist or contains no VERSION file, giving you a
  chance to correct or discard the entry.

## Version format: optional list fields

- A trailing comma in a list definition (e.g. `alpha, beta,`) marks the field
  as optional. Bumping cycles through the values and back to empty:
  `` → `alpha` → `beta` → `` → …
  Without a trailing comma the field is required and wraps from last back to first.

## Git hook: smarter staleness detection

- The pre-commit hook now uses `git diff --cached --name-only` to check whether
  VERSION is part of the staged files — no more false positives from touching an
  old file after bumping.
- The staged-file check uses the full path relative to the git root, so in
  monorepos a neighbouring project's VERSION can no longer produce a false positive.
- The re-check after bumping uses `git log` to compare VERSION's mtime against the
  last commit, which is the correct question ("was VERSION bumped since last commit?").
- The main-window status indicator also uses the last commit time as its threshold.
- All three fall back to the old mtime comparison when git is unavailable.
- Git operations now time out after 5 seconds to prevent the UI or hook from
  hanging in corporate environments with slow/authenticated remotes.

## Git hook: ✓ Bump & Commit now actually stages VERSION

- After bumping, the hook runs `git add VERSION` before exiting so the version
  bump and the code change land in the same commit, as intended.

## Git hook: info banner

- When the hook blocks a commit, a banner now explains exactly why:
  "N staged file(s) don't include a version bump." (or a fallback text when git
  is unavailable), plus a hint to bump and click ✓ Bump & Commit.

## Removed Phosphor icon font

- The bundled Phosphor.ttf / Phosphor-Bold.ttf were never used in the UI.
  Removing them shrinks the executable by ~600 KB.

---

## Was ist neu in vNEXT

## Verbesserungen

- einige Details bzgl. Lokalisierung gelöst

## CLI: `--help`-Flag

- `VerBump.exe --help` (auch `-h` / `/?`) gibt eine Verwendungsübersicht aus und
  beendet das Programm — praktisch beim Aufruf aus Terminal oder CI-Skript.

## Einstellungen: Listen-Referenzfenster

- Ein `?`-Button neben dem Listen-Feld öffnet ein nicht-modales Referenzfenster
  mit Dokumentation zur Listen-Syntax, Bereichsexpansion (`prefix{1-3}` →
  `prefix1, prefix2, prefix3`, `{01-09}` für führende Nullen) und Verwendung
  in Format-Strings — ohne den Einstellungs-Dialog verlassen zu müssen.

## Einstellungen: Validierung beim Speichern

- Vor dem Speichern prüft VerBump jeden Projekteintrag und warnt, wenn das
  konfigurierte Verzeichnis nicht existiert oder keine VERSION-Datei enthält —
  mit der Möglichkeit, den Eintrag zu korrigieren oder trotzdem zu speichern.

## Versionsformat: optionale Listen-Felder

- Ein abschließendes Komma in einer Listen-Definition (z.B. `alpha, beta,`)
  macht das Feld optional. Bumpen läuft durch Leer → Werte → Leer → …:
  `` → `alpha` → `beta` → `` → …
  Ohne abschließendes Komma ist das Feld erforderlich und springt vom letzten
  Wert zurück zum ersten.

## Git-Hook: präzisere Aktualitätserkennung

- Der Pre-Commit-Hook fragt nun per `git diff --cached --name-only` ab, ob VERSION
  in den gestageten Dateien enthalten ist — keine Fehlalarme mehr, wenn nach dem
  Bump noch eine alte Datei angefasst wird.
- Der Staged-File-Check verwendet den vollständigen Pfad relativ zum Git-Root,
  sodass in Monorepos eine VERSION eines anderen Projekts keinen Fehlalarm auslöst.
- Der Re-Check nach dem Bump vergleicht VERSION's mtime mit dem letzten Commit-
  Zeitstempel — die eigentlich richtige Frage ("wurde VERSION seit dem letzten
  Commit geändert?").
- Der Status-Indikator im Hauptfenster nutzt ebenfalls den letzten Commit als
  Schwellenwert.
- Alle drei fallen auf den alten mtime-Vergleich zurück, wenn git nicht verfügbar ist.
- Git-Operationen haben nun ein 5-Sekunden-Timeout, damit der Hook/UI in
  Corporate-Umgebungen mit langsamen Remotes nicht hängt.

## Git-Hook: ✓ Bump & Commit stagt VERSION jetzt wirklich

- Nach dem Bump führt der Hook `git add VERSION` aus, bevor er mit exit 0 endet —
  Versionsbump und Code-Änderungen landen damit im selben Commit.

## Git-Hook: Info-Banner

- Wenn der Hook einen Commit blockiert, erklärt ein Banner jetzt genau warum:
  „N gestagete Datei(en) ohne Versions-Bump." (oder ein Fallback-Text ohne git),
  plus ein Hinweis zum Bumpen und Klicken auf ✓ Bump & Commit.

## Phosphor-Schriftarten entfernt

- Die eingebetteten Phosphor.ttf / Phosphor-Bold.ttf wurden nie im UI verwendet.
  Entfernung verkleinert die EXE um ca. 600 KB.
