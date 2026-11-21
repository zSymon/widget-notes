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
- **Größe frei ziehen** – über den Griff unten rechts, rastet ins Raster ein; Schnellwahl Klein, Mittel, Groß, Hoch
- **Wächst mit** – läuft Text unten heraus, wird die Kachel automatisch größer
- **Bis an den Rand** – Kacheln lassen sich direkt an jeden Bildschirmrand setzen
- **Automatische Updates** – neue Versionen werden erkannt und per Klick installiert
- **Notiz oder Liste** – Aufgaben mit Haken, erledigte werden durchgestrichen
- **Termine** – Fälligkeitsdatum und Uhrzeit per Mini-Kalender, Markierung für heute und überfällig
- **Erinnerungen** – Windows-Benachrichtigung, wenn eine Aufgabe fällig ist
- **Anpassbar** – 8 Designs, eigene Verlaufs- und Akzentfarben, Transparenz, Eckenradius, Schriftart, Schriftgröße, Schatten und Glanz
- **Merkt sich alles** – jede Kachel bleibt auch nach einem Neustart auf ihrem Bildschirm und Platz; ist ein Bildschirm aus, wartet sie auf dem Hauptbildschirm und kehrt automatisch zurück
- **Autostart** – per Schalter im Menü der Kachel
- **Speichert automatisch** – alles landet in `%APPDATA%\WidgetNotes\notes.json`

## Bedienung

| Aktion | So geht's |
|---|---|
| Kachel verschieben | An einer freien Stelle (nicht im Text) ziehen |
| Größe ändern | Griff unten rechts in der Kachel ziehen |
| Neue Notiz | **+** in der Kachel oder <kbd>Strg</kbd>+<kbd>N</kbd> |
| Größe, Typ, Design, Löschen | **···** in der Kachel |
| Neue Aufgabe | <kbd>Enter</kbd> in einer Aufgabe oder in „Neue Aufgabe“ |
| Aufgabe löschen | <kbd>Rücktaste</kbd> in einer leeren Aufgabe |
| Termin setzen | Beim Überfahren einer Aufgabe auf das Kalender-Symbol klicken |
| Schriftgröße | <kbd>Strg</kbd> + Mausrad |
| Desktop anzeigen | Linksklick auf das Symbol im Infobereich der Taskleiste |
| Mit Windows starten | **···** → Schalter „Mit Windows starten“ (oder Rechtsklick auf das Symbol im Infobereich) |
| Beenden | Rechtsklick auf das Symbol im Infobereich der Taskleiste |

> Tipp: Leg die `WidgetNotes.exe` vor dem Aktivieren des Autostarts an einen festen Ort (z. B. einen eigenen Ordner), denn der Autostart merkt sich den Speicherort.

## Selbst bauen

Voraussetzung: [.NET SDK](https://dotnet.microsoft.com/download) (11 oder neuer)

```powershell
# Starten zum Entwickeln
dotnet run

# Einzelne, eigenständige WidgetNotes.exe nach .\dist bauen
.\publish.ps1
```
