---
icon: fa-solid fa-paperclip
description: "Lade Dateien wie 3D-Modelle, Code, Diffs, Bilder und Dokumente hoch, sieh sie an, teile sie und lass Marshal sie zeigen."
---

# Mit Artefakten arbeiten

Lade Dateien wie 3D-Modelle, Code, Diffs, Bilder und Dokumente hoch, sieh sie an, teile sie und lass Marshal sie zeigen.

## Bevor du anfängst

- Melde dich in der Weboberfläche an. Hochladen, Auflisten und Löschen brauchen eine Browser-Sitzung.
- Optional: [Displays](displays.md), um Artefakte auf einem Fernseher zu zeigen, und ein Modell mit `vision: true` für `artifacts_inspect`.

## Was Artefakte sind

Ein Artefakt ist eine Datei, die Marshal erzeugt oder bekommen hat, gespeichert unter `/data/artifacts/<owner>/` mit einer festen ID. Das Artefakt eines anderen Benutzers verhält sich wie nicht gefunden.

| Quelle | Wie es ankommt | Belastet |
|---|---|---|
| `upload` | Hochladen in der Weboberfläche | Nein |
| `printer` | Du behältst einen Drucker-Snapshot | Nein |
| `executor` | Eine Coding-Sitzung lädt Ausgaben hoch | Ja |
| `mail` | `mail.save_attachment` | Ja |
| `github` | Diffs und Logs fehlgeschlagener Jobs, gespeichert von den Werkzeugen `repos` | Ja |

Belastete Artefakte stammen aus Inhalten Dritter. Die Liste markiert sie. Siehe [nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md).

## Schritte

### Eine Datei hochladen

1. Öffne **Aktivität > Artefakte**.
2. Wähle Hochladen und such eine Datei aus (höchstens `artifacts.maxUploadMb`, Standard 200 MB). Eine größere Datei wird mit 413 abgelehnt.

Neue Artefakte erscheinen sofort in der Liste, ohne Neuladen.

### Finden und filtern

Die Liste zeigt die neuesten zuerst, mit Name, Art, Quelle, Größe, Erstellungszeit und der Markierung für Belastung. Filtere nach Art und Quelle, suche nach Name. Bilder und 3D-Modelle zeigen ein Vorschaubild; andere Zeilen zeigen das Symbol ihrer Art. Die Nutzung des Kontingents wird auch angezeigt.

### Öffnen und ansehen

| Art | Viewer |
|---|---|
| `image`, `video` | Der eingebaute Player des Browsers. |
| `svg` | Als Bild gezeigt, nie inline. |
| `code` | Hervorgehobener Text mit Zeilennummern, Einrückungslinien, Sprache und Zeilenzahl, einem Schalter für Umbruch (pro Browser gemerkt) und einer Kopierschaltfläche. |
| `diff` | Diff-Ansicht mit hervorgehobenem Code. |
| `model3d` | Interaktive 3D-Ansicht für STL und 3MF: drehen, zoomen, zurücksetzen. Lädt nur, wenn du ein 3D-Artefakt öffnest. |
| `pdf` | Der PDF-Viewer des Browsers in einem neuen Tab. |
| `markdown` | Dargestellt wie im Chat: HTML aus, nur Bilder vom selben Ursprung, Codeblöcke hervorgehoben. |
| `html` | Sandbox-Frame ohne Skripte. |
| `other` | Name, Größe und eine Download-Schaltfläche. |

Jeder Viewer bietet einen Download an und zeigt Quelle und Belastungsmarkierung.

### Auf Zeilen verlinken

Klicke im Code-Viewer auf eine Zeilennummer, um sie auszuwählen (`#L12`), mit Umschalt-Klick erweiterst du die Auswahl (`#L12-L20`). Öffne eine Adresse wie `/artifacts/<id>#L12-L20`, um zu diesen Zeilen zu springen. Ein Fragment außerhalb der Datei wird ignoriert.

### Löschen

Wähle Löschen und bestätige im Dialog. Die Zeile und die Datei werden entfernt, und ihre Links funktionieren nicht mehr.

## Wie Arten erkannt werden

Marshal entscheidet die Art anhand der Dateiendung und der ersten Bytes der Datei, nie allein anhand eines angegebenen Medientyps. Eine Datei, deren Inhalt nicht zu ihrer Endung passt, wird als `other` gespeichert: Ein `photo.png`, das HTML enthält, ist `other` und wird heruntergeladen statt dargestellt.

Arten: `image` (PNG, JPEG, GIF, WebP), `svg`, `video` (MP4, WebM), `model3d` (STL, 3MF), `code`, `diff` (`.diff`, `.patch`), `pdf`, `markdown`, `html`, `other`.

## Syntaxhervorhebung

Der Web-Viewer und die Fernsehseiten teilen sich einen Highlighter und eine Definitionsdatei, eine Datei sieht also in beiden gleich aus. Sprachen: C#, TypeScript, JavaScript, XML (auch `.csproj`, `.props`, `.vue`, `.html`, `.svg`), Python, OpenSCAD, YAML, JSON, TOML und INI, Bash, PowerShell, C, C++, Rust, Go, Java, Kotlin, Swift, Ruby, PHP, SQL, CSS, SCSS, G-Code, Dockerfile und Markdown. Die Sprache ergibt sich aus dem Dateinamen; eine unbekannte Endung bleibt ohne Hervorhebung. Klammern werden nach Verschachtelungstiefe eingefärbt. Diffs nehmen die Sprache jeder Datei aus ihrem Kopf.

## Sichere Auslieferung

Antworten mit Inhalt tragen den erkannten Medientyp und `X-Content-Type-Options: nosniff`. `html` und `svg` werden mit einer Sandbox ausgeliefert, die keine Skripte, Formulare oder Pop-ups erlaubt. `other` ist immer ein Download.

## Links

Die Inhalts-URL braucht deine Sitzung oder ein Token. Marshal kann auch signierte Links ausstellen, die keine Anmeldung brauchen, an ein Artefakt gebunden sind und nach `artifacts.signedLinkMinutes` ablaufen (Standard 15), zum Beispiel für eine Benachrichtigung aufs Handy. Abgelaufene, veränderte oder fremde Links bekommen 404.

## Kontingent

Jeder Benutzer hat `artifacts.quotaMb` (Standard 5120 MB). Ein Schreibvorgang über das Kontingent schlägt mit einem Fehler fehl, der Kontingent und aktuelle Nutzung nennt, und speichert nichts. Marshal behält Artefakte, bis du sie löschst.

```yaml
artifacts:
  quotaMb: 5120
  maxUploadMb: 200
  signedLinkMinutes: 15
```

Siehe [`artifacts`](../configuration/marshal-yaml/artifacts.md).

## Was Marshal mit Artefakten tun kann

| Werkzeug | Stufe | Wirkung |
|---|---|---|
| `artifacts_list(kind?, source?, query?)` | safe | Bis zu 50 neueste Treffer. Die Ausgabe ist nicht vertrauenswürdig und belastet den Durchlauf. |
| `artifacts_show(id, page?)` | safe | Zeigt das Artefakt in einem Web-Turn im Chat (Bilder inline, andere Arten als Karte, die den Viewer öffnet). Spielt in einem Satelliten-Turn mit Display seine Darstellung auf diesem Display ab. In anderen Turns verborgen. |
| `artifacts_inspect(id, question, page?)` | safe | Schickt Seite `page` der Darstellung als Bild an ein Vision-Modell und liefert die Antwort, zum Beispiel „Sind die Befestigungslöcher auf beiden Seiten?“ Verborgen, wenn weder die Stufe des Durchlaufs noch `standard` ein Vision-Modell hat. Schlägt für Video und Arten ohne Bilddarstellung mit `artifacts.no_rendition` fehl. |

Die Ausgabe von `artifacts_inspect` ist nicht vertrauenswürdig und belastet den Durchlauf. Fehler: nicht gefunden für unbekannte oder fremde IDs, `artifacts.invalid_arguments` für eine Seite außerhalb der Darstellung.

Chat-Karten bleiben bei der Nachricht, wenn du die Sitzung wieder öffnest. Vorschauen (`GET /api/v1/artifacts/{id}/preview`) sind PNGs bis 480x270 für Bilder und 3D-Modelle; andere Arten antworten mit 404.

## Fehlerbehebung

- Hochladen mit 413 abgelehnt: Die Datei ist größer als `artifacts.maxUploadMb`.
- Fehler beim Kontingent: Lösche Artefakte oder erhöhe `artifacts.quotaMb`.
- `artifacts_inspect` fehlt: kein Modell mit `vision: true`. Siehe [Modelle einrichten](../installation/models-setup.md).
- 3D-Modell ohne Vorschaubild: Die Datei lässt sich nicht lesen oder hat keine Dreiecke.

## Verwandte Seiten

::: cards
- [Displays](displays.md)
- [Arten und Werkzeuge für Artefakte](../reference/tools.md)
- [Drucker](integrations/printers-moonraker.md)
- [GitHub](integrations/github.md)
- [Mail](integrations/mail.md)
:::
