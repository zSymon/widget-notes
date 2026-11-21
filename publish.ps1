# Erstellt eine einzelne, eigenständige WidgetNotes.exe im Ordner .\dist
# (läuft auf jedem Windows 10/11 x64, ohne dass .NET installiert sein muss)
dotnet publish "$PSScriptRoot\WidgetNotes.csproj" -c Release -o "$PSScriptRoot\dist"
