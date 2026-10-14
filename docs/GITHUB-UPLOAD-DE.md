# Auf GitHub hochladen

## Repository anlegen

Name: **SAMEnhanced**
Beschreibung: Inhalt aus `GITHUB_DESCRIPTION.txt` kopieren.
Sichtbarkeit: Public, wenn alle es herunterladen können sollen.
Keine zusätzliche README, .gitignore oder Lizenz erzeugen: Diese Dateien sind bereits enthalten.

## Welche Dateien?

`SAMEnhanced-GitHub.zip` entpacken. Den **Inhalt** des Ordners `SAMEnhanced` ins Repository laden, damit die README direkt im Hauptverzeichnis liegt.

Alle enthaltenen Dateien gehören dazu, einschließlich `.github`, `.gitignore`, `.gitattributes`, `LICENSE.txt` und der ursprünglichen Lizenzhinweise in den Quellen. Die Windows-ZIP gehört als Download in **Releases**.

## Upload im Browser

GitHub erlaubt höchstens 100 Dateien pro Browser-Upload. Dieses Projekt hat mehr Dateien: deshalb in mehreren Durchgängen hochladen.

1. Im Repository **Add file → Upload files** wählen.
2. Zuerst die einzelnen Dateien aus dem Hauptverzeichnis und die Ordner `assets`, `docs` und `.github` hineinziehen. **Commit changes** wählen.
3. Danach jeden der fünf Projektordner einzeln hochladen: `SAM.API`, `SAM.Enhanced`, `SAM.Enhanced.Tests`, `SAM.Game`, `SAM.Picker`. Jeweils den **ganzen Ordner** hineinziehen und committen, damit die Struktur erhalten bleibt. Jeder dieser Ordner hat weniger als 100 Dateien.
4. Am Ende liegt `README.md` direkt neben `SAM.sln` und den Projektordnern. Die Vorschau sollte Banner und Screenshot anzeigen.
5. Unter **Actions** den Windows-Build prüfen. Zwischenstände während der einzelnen Uploads können vorübergehend fehlschlagen; entscheidend ist der Lauf nach dem letzten Upload.

Quelle: https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository

## Release erstellen

1. **Releases → Draft a new release** öffnen.
2. Neuen Tag `v7.0.41.1` auf dem vollständigen Hauptbranch erstellen.
3. Titel: **SAM Enhanced v7.0.41.1**.
4. Inhalt aus `RELEASE_NOTES.md` in das Beschreibungsfeld kopieren.
5. `SAMEnhanced-v7.0.41.1-windows-x86.zip` und `SAMEnhanced-SHA256SUMS.txt` als Dateien anhängen.
6. **Publish release** wählen.

Quelle: https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository

## About / Topics

Beschreibung aus `GITHUB_DESCRIPTION.txt`. Optional als Website: `https://discord.gg/JaRSAWTUMx`.
Topics: `steam`, `achievements`, `achievement-manager`, `csharp`, `winforms`, `windows`, `bulk-actions`.

## Credits

**leanz** — https://discord.gg/JaRSAWTUMx
Originalsoftware: **Rick / gibbed**. Die Original-Lizenz bleibt erhalten; diese Version ist klar als unabhängige Überarbeitung gekennzeichnet.

