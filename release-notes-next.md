# What's new in vNEXT

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

## Improvements

- Settings dialog: Scheme dropdown and "Reset on bump" checkbox replaced by a
  single Format field (always visible) with a tooltip showing syntax examples.
- Status bar shows the active format string instead of just the scheme name.

---

## Was ist neu in vNEXT

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

## Verbesserungen

- Einstellungs-Dialog: Schema-Dropdown und „Reset on bump"-Checkbox wurden durch
  ein einzelnes Format-Feld (immer sichtbar) mit Tooltip-Syntaxbeispielen ersetzt.
- Statusleiste zeigt den aktiven Format-String statt nur den Schema-Namen.
