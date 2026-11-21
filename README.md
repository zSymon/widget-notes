<p align="center">
  <img src="Assets/icon-256.png" width="96" alt="Widget Notes" />
</p>

<h1 align="center">Widget Notes</h1>

<p align="center">Notizen und To-dos als schicke Kacheln direkt auf dem Windows-Desktop – ähnlich wie die Widgets unter macOS.</p>

---

## Download

**[⬇ WidgetNotes.exe herunterladen](https://github.com/zSymon/widget-notes/releases/latest/download/WidgetNotes.exe)**

Einfach starten – keine Installation, kein .NET nötig. Läuft unter Windows 10 und 11 (64 Bit).

> Beim ersten Start kann Windows SmartScreen warnen („Der Computer wurde durch Windows geschützt“), weil die App nicht signiert ist. Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

## Funktionen

- **Echte Desktop-Kacheln** – liegen auf dem Desktop unter allen Fenstern, bleiben bei <kbd>Win</kbd>+<kbd>D</kbd> sichtbar, tauchen nicht in Taskleiste oder Alt+Tab auf
- **Raster mit Einrasten** – Kacheln beim Ziehen per Vorschau platzieren, sie weichen sich automatisch aus
- **Vier Größen** – Klein, Mittel, Groß, Hoch
- **Notiz oder Liste** – Aufgaben mit Haken, erledigte werden durchgestrichen
- **Termine** – Fälligkeitsdatum und Uhrzeit per Mini-Kalender, Markierung für heute und überfällig
- **Erinnerungen** – Windows-Benachrichtigung, wenn eine Aufgabe fällig ist
- **Anpassbar** – 8 Designs, eigene Verlaufs- und Akzentfarben, Transparenz, Eckenradius, Schriftart, Schriftgröße, Schatten und Glanz
- **Speichert automatisch** – alles landet in `%APPDATA%\WidgetNotes\notes.json`

## Bedienung

| Aktion | So geht's |
|---|---|
| Kachel verschieben | An einer freien Stelle (nicht im Text) ziehen |
| Neue Notiz | **+** in der Kachel oder <kbd>Strg</kbd>+<kbd>N</kbd> |
| Größe, Typ, Design, Löschen | **···** in der Kachel |
| Neue Aufgabe | <kbd>Enter</kbd> in einer Aufgabe oder in „Neue Aufgabe“ |
| Aufgabe löschen | <kbd>Rücktaste</kbd> in einer leeren Aufgabe |
| Termin setzen | Beim Überfahren einer Aufgabe auf das Kalender-Symbol klicken |
| Schriftgröße | <kbd>Strg</kbd> + Mausrad |
| Desktop anzeigen | Linksklick auf das Symbol im Infobereich der Taskleiste |
| Autostart, Beenden | Rechtsklick auf das Symbol im Infobereich der Taskleiste |

## Selbst bauen

Voraussetzung: [.NET SDK](https://dotnet.microsoft.com/download) (11 oder neuer)

```powershell
# Starten zum Entwickeln
dotnet run

# Einzelne, eigenständige WidgetNotes.exe nach .\dist bauen
.\publish.ps1
```
