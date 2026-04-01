# What's new in vNEXT

## New features

- **Install Git Hook from context menu**: Right-clicking a project row now shows "Install Git Hook" / "Remove Git Hook" directly — no need to open Settings. Works for both managed and unsaved (context-menu-opened) projects. Success is confirmed in the status bar.

## Improvements

- **"Bump & Commit" button disabled until version is changed**: In Git-hook mode the button is now greyed out until at least one version has actually been bumped, preventing accidental empty commits.
- **Button widths auto-sized**: All three bottom buttons ("Bump & Commit", "Block Commit", "Skip This Time") now measure their label text and grow as needed — no more clipped text in any language.
- **Consistent button font**: All three bottom buttons now use the same font size (Segoe UI 9.5 pt) so their text baselines align.
- **Tooltips in Git-hook mode**: All three buttons now show a tooltip: "Bump & Commit: Enter", "Block Commit: Escape", and "Commit without version bump (no shortcut)".
- **Right margin of Save/Commit button**: Slightly increased spacing from the window edge.

---

## Was ist neu in vNEXT

## Neue Features

- **Git-Hook direkt aus dem Kontextmenü installieren**: Rechtsklick auf eine Projektzeile zeigt jetzt "Git-Hook installieren" / "Git-Hook entfernen" — ohne Umweg über die Einstellungen. Funktioniert auch für Projekte, die per Kontextmenü geöffnet wurden (unsaved). Erfolg wird in der Statuszeile bestätigt.

## Verbesserungen

- **"Bump & Commit"-Button erst aktiv nach einem Bump**: Im Git-Hook-Modus ist der Button nun deaktiviert, bis mindestens eine Version tatsächlich erhöht wurde — kein versehentlicher Commit ohne Versionsänderung mehr.
- **Button-Breiten automatisch berechnet**: Alle drei unteren Buttons ("Bump & Commit", "Commit stoppen", "Commit ohne Bump") messen ihren Text und wachsen bei Bedarf — kein abgeschnittener Text mehr in irgendeiner Sprache.
- **Einheitliche Schriftgröße der Buttons**: Alle drei unteren Buttons verwenden jetzt dieselbe Schriftgröße (Segoe UI 9,5 pt), sodass die Textbaselines fluchten.
- **Tooltips im Git-Hook-Modus**: Alle drei Buttons zeigen jetzt einen Tooltip: "Bump & Commit: Enter", "Commit stoppen: Escape" und "Commit ohne Versionsbump (kein Shortcut)".
- **Rechter Abstand des Speichern-Buttons**: Etwas mehr Abstand vom Fensterrand.
