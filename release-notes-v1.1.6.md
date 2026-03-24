# What's new in v1.1.6

## New features

- **Unified format string** — version schemes are now defined by a single format
  string instead of a scheme dropdown. Examples:
  - `[sem]` — SemVer shorthand, equivalent to `[{#major}.{#minor}.{#patch}]`
  - `{YYYY}.{MM}.{#build}` — CalVer with auto-date fields
  - `{YYYY}.[{#major}.{#minor}.{#patch}]` — year prefix + SemVer reset group
  - `[{#major}.{#minor}]-{alpha|beta|prod}` — numeric group + inline list
  - `[*8]` — free-text field, max 8 characters

- **Reset groups via `[...]`** — wrapping tokens in square brackets creates a
  cascade: bumping a component resets all subsequent components in the same group
  to zero (or to the first list value). Outside `[...]`, all components are
  independent. `[sem]` is shorthand for `[{#major}.{#minor}.{#patch}]`.

- **Inline lists** — `{alpha|beta|prod}` cycles through the defined values when
  bumped; resets to the first value when a preceding token in its reset group is bumped.

- **Date tokens** — `{YYYY}`, `{YY}`, `{MM}`, `{DD}` auto-fill with today's date
  on every refresh; no button is generated for them.

- **Backward compatible** — existing `semver`, `calver`, and `sequential` settings
  are automatically migrated to the new format string on first load; no changes
  to existing `settings.json` files required.

- **Menu bar replaces toolbar** — the compact icon toolbar is replaced by a
  standard menu bar (File / Edit / View / ?), giving keyboard users full
  Alt-key access to all commands.

- **Undo (Ctrl+Z)** — every bump and "Sync to highest" operation is now
  undoable. The Edit menu shows the action label (e.g. *Undo: patch+*) so
  you always know what will be reverted.

- **Settings-file and VERSION history** — File › Open Settings File and
  File › Open VERSION File each maintain a configurable recent-files list
  (default length 6, configurable in Settings › Global).

- **Favorites** — frequently used settings or VERSION files can be pinned as
  favorites (★) via the Open … menus and appear at the top of the list.

- **"Sync to highest"** — moved from toolbar to Edit menu; sets all visible
  projects to the highest version among them (respects the current filter).

- **Quick-edit from main window** — double-click on a project row (name, icon
  or status bar) to jump directly to that entry in the Settings dialog.
  Right-click opens a context menu with "Edit Settings", "Open Folder in Explorer",
  and (for unsaved entries) "Add to Settings".

- **Named lists** — define reusable value lists globally in Settings under
  "Lists" (one per line: `stage: alpha, beta, prod`) and reference them in
  any format string as `{stage}`. Bump button cycles through the defined values.

- **Git hook improvements** — the hook dialog now shows only the stale project
  instead of all projects in the settings file; a new **↷ Skip This Time** button
  lets you bypass the check and commit without bumping (useful for WIP or
  documentation-only commits). Corporate deployments can disable the bypass option
  machine-wide via `C:\ProgramData\VerBump\policy.json`:

  ```json
  { "allowHookBypass": false }
  ```

  When set, only **Bump & Commit** and **Block Commit** are shown.

## Improvements

- **Explorer context menu for settings files** now matches `verbump-settings.json`
  only (previously matched any `settings.json`, which was too broad and caused the
  entry to appear on unrelated files such as VS Code or npm configs). Rename your
  VerBump settings file to `verbump-settings.json` to get the context menu entry.

- Settings dialog: Scheme dropdown and "Reset on bump" checkbox replaced by a
  single Format field (always visible) with a tooltip showing syntax examples.
  The Format field now shows a live preview and highlights parse errors; a **?**
  button opens the format-string documentation directly.
- Status bar shows the active format string instead of just the scheme name.

---

## Was ist neu in v1.1.6

## Neue Features

- **Einheitlicher Format-String** — Versionsschemas werden jetzt durch einen einzigen
  Format-String definiert, anstatt über ein Schema-Dropdown. Beispiele:
  - `[sem]` — SemVer-Shorthand, entspricht `[{#major}.{#minor}.{#patch}]`
  - `{YYYY}.{MM}.{#build}` — CalVer mit automatischen Datumsfeldern
  - `{YYYY}.[{#major}.{#minor}.{#patch}]` — Jahres-Präfix + SemVer-Reset-Gruppe
  - `[{#major}.{#minor}]-{alpha|beta|prod}` — Zahlengruppe + Inline-Liste
  - `[*8]` — Freitextfeld, max. 8 Zeichen

- **Reset-Gruppen via `[...]`** — Tokens in eckigen Klammern bilden eine Kaskade:
  Erhöhen einer Komponente setzt alle nachfolgenden Komponenten in der gleichen
  Gruppe auf null (bzw. auf den ersten Listenwert) zurück. Außerhalb von `[...]`
  sind alle Komponenten unabhängig. `[sem]` ist Kurzform für
  `[{#major}.{#minor}.{#patch}]`.

- **Inline-Listen** — `{alpha|beta|prod}` durchläuft beim Bump die definierten Werte;
  setzt beim Bump einer vorhergehenden Komponente in der gleichen Reset-Gruppe auf den
  ersten Wert zurück.

- **Datums-Tokens** — `{YYYY}`, `{YY}`, `{MM}`, `{DD}` werden bei jedem Refresh
  automatisch mit dem aktuellen Datum befüllt; es wird kein Button dafür erzeugt.

- **Rückwärtskompatibel** — bestehende `semver`-, `calver`- und `sequential`-Settings
  werden beim ersten Laden automatisch auf den neuen Format-String migriert;
  bestehende `settings.json`-Dateien müssen nicht angepasst werden.

- **Menüleiste ersetzt Toolbar** — die kompakte Icon-Toolbar wird durch eine
  Standard-Menüleiste (Datei / Bearbeiten / Ansicht / ?) ersetzt; alle Befehle
  sind damit per Alt-Taste erreichbar.

- **Rückgängig (Strg+Z)** — jeder Bump und „Auf höchste sync." ist widerrufbar.
  Das Bearbeiten-Menü zeigt das Aktions-Label (z. B. *Rückgängig: patch+*).

- **Settings- und VERSION-Verlauf** — Datei › Settings-Datei öffnen und
  Datei › VERSION öffnen führen jeweils eine konfigurierbare Verlaufsliste
  (Standard 6, änderbar in Einstellungen › Global).

- **Favoriten** — häufig genutzte Settings- oder VERSION-Dateien lassen sich
  als Favorit (★) anheften und erscheinen künftig oben in der Liste.

- **„Auf höchste sync."** — in das Bearbeiten-Menü verschoben; setzt alle
  sichtbaren Projekte auf die höchste Version darunter.

- **Schnellzugriff auf Einstellungen** — Doppelklick auf eine Projektzeile
  öffnet den Einstellungs-Dialog direkt auf diesem Eintrag. Rechtsklick zeigt
  ein Kontextmenü mit „Einstellungen bearbeiten", „Ordner im Explorer öffnen"
  und (für unsaved Einträge) „Zu Settings hinzufügen".

- **Benannte Listen** — wiederverwendbare Wertlisten global in den Einstellungen
  unter „Lists" definieren (eine pro Zeile: `stage: alpha, beta, prod`) und im
  Format-String als `{stage}` referenzieren. Der Bump-Button durchläuft die Werte.

- **Git-Hook-Verbesserungen** — der Hook-Dialog zeigt jetzt nur das betroffene
  Projekt statt aller Settings-Einträge; ein neuer Button **↷ Jetzt überspringen**
  erlaubt es, den Check zu überspringen und ohne Bump zu commiten (nützlich für
  WIP-Commits oder reine Doku-Änderungen). Corporate-Deployments können den Bypass
  maschinenübergreifend deaktivieren via `C:\ProgramData\VerBump\policy.json`:

  ```json
  { "allowHookBypass": false }
  ```

  Bei `false` sind nur **Bump & Commit** und **Commit stoppen** sichtbar.

## Verbesserungen

- **Explorer-Kontextmenü für Settings-Dateien** greift jetzt nur noch bei
  `verbump-settings.json` (bisher bei jeder `settings.json`, was zu unerwünschten
  Einträgen in VS-Code-, npm- o. ä. Dateien führte). Zum Nutzen des Kontextmenüs
  die VerBump-Settings-Datei in `verbump-settings.json` umbenennen.

- Einstellungs-Dialog: Schema-Dropdown und „Reset on bump"-Checkbox wurden durch
  ein einzelnes Format-Feld (immer sichtbar) mit Tooltip-Syntaxbeispielen ersetzt.
  Das Format-Feld zeigt jetzt eine Live-Vorschau und hebt Parse-Fehler hervor;
  ein **?**-Button öffnet die Format-String-Dokumentation direkt.
- Statusleiste zeigt den aktiven Format-String statt nur den Schema-Namen.release-notes-v1.1.4.md