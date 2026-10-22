---
icon: fa-solid fa-note-sticky
description: "Lass Marshal deine Markdown-Notizen aus einem Git-Repository durchsuchen und lesen, getrennt von seinem Gedächtnis."
---

# Deine Notizen verbinden

Lass Marshal deine Markdown-Notizen aus einem Git-Repository durchsuchen und lesen, getrennt von seinem Gedächtnis.

## Bevor du anfängst

- Ein Git-Repository mit Markdown-Dateien. Nur `.md`-Dateien werden indiziert.
- Entweder ein Repository, das schon in `/data/notes` liegt, oder ein Remote, das Marshal dorthin klont.
- Für ein privates Remote: ein HTTPS-Zugriffstoken oder ein SSH-Deploy-Key (privater Schlüssel), als Secret gespeichert.
- Ein mehrsprachiges Embedding-Modell, siehe [Modelle einrichten](../../installation/models-setup.md). Die Suche ist hybrid: Volltext und Embeddings.

## Schritte

::: tabs
== Lokales Repository
Leg das Repository nach `/data/notes` (das Volume `/data`). Notizen sind an, sobald dieses Verzeichnis existiert. Keine Konfiguration nötig.
== Remote-Repository
```yaml
notes:
  remote: https://git.example.com/lukas/notes.git
  branch: main
  credentialSecret: notes_token
  pullInterval: "00:15:00"
```

Marshal klont das Repository beim ersten Start nach `/data/notes` und zieht es alle `pullInterval`. Es ist ein **nur lesbarer Spiegel**: Marshal committet und pusht nie. Halte `credentialSecret` als Secret-Namen. Der Wert ist ein HTTPS-Token oder ein SSH-Deploy-Key. Lass ihn für ein öffentliches Repository weg. Schlüssel und Grenzen stehen in der [Referenz von `notes`](../../configuration/marshal-yaml/integrations.md#notes).

Starte Marshal nach einer Änderung an `marshal.yaml` neu.
:::

## Werkzeuge

Alle Notizwerkzeuge sind safe. Ihre Ergebnisse sind nicht vertrauenswürdig und sensibel, das Lesen einer Notiz belastet den Durchlauf also mit der Quelle `notes`.

| Werkzeug | Was es tut |
|---|---|
| `notes.list(folder?)` | Listet Notizen, optional in einem Ordner (höchstens 500). |
| `notes.read(path)` | Liest eine Notiz. Text wird bei `notes.maxReadCharacters` abgeschnitten (Standard 50000). |
| `notes.search(query, limit?)` | Hybride Suche nach Bedeutung und Stichworten. Liefert passende Abschnitte. `limit` 1 bis 20. |

Pfade sind auf das Repository beschränkt: Absolute Pfade, `..`, versteckte Einträge wie `.git` und symbolische Links werden abgewiesen, und nichts außerhalb des Repositorys wird gelesen. Es gibt keine schreibenden Werkzeuge.

## Notizen sind kein Gedächtnis

Marshal legt Notizen nie von sich aus in den Kontext eines Turns. Eine Notiz erreicht das Modell nur, wenn es ein Notizwerkzeug aufruft. Inhalte kommen nur ins Gedächtnis, wenn der Agent einen Eintrag in eigenen Worten schreibt, mit `source_ref` auf den Pfad der Notiz, und ein belasteter Durchlauf fragt vorher nach einer Bestätigung. Siehe [Gedächtnis](../memory.md).

Der Notizindex (Pfad, Überschrift, Text, Embedding) sind abgeleitete Daten. Er wird nicht exportiert oder gesichert, und Marshal baut ihn aus dem Repository neu auf. Nach einem Pull werden nur die geänderten Dateien neu indiziert.

## Grenzen

- Nur Markdown-Dateien. Andere Dateien werden ignoriert.
- Änderungen in einem Remote erscheinen nach dem nächsten Pull (Standard 15 Minuten, mindestens 1 Minute).
- Die Qualität der Suche hängt vom Embedding-Modell ab.

## Prüfen, ob es funktioniert

1. Frag „was habe ich über das Garagenprojekt geschrieben?“ Das Modell ruft `notes_search` auf.
2. Bitte es, den besten Treffer zu öffnen. Es ruft `notes_read` mit einem Pfad wie `projects/garage.md` auf.
3. Bei einem Remote: Push eine Änderung und warte `pullInterval` ab. Der neue Text ist durchsuchbar.

## Fehlerbehebung

- **Keine Werkzeuge `notes_`.** Es gibt kein `remote`, und `/data/notes` existiert nicht.
- **Das Klonen scheitert.** Prüfe die URL und `credentialSecret`. Ein SSH-Schlüssel muss ein privater Schlüssel sein.
- **Eine Notiz fehlt in der Suche.** Sie ist keine `.md`-Datei, oder der Index hat noch nicht aufgeholt.

## Verwandte Seiten

::: cards
- [Gedächtnis](../memory.md)
- [Nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md)
- [Volumes und Aufbau](../../installation/volumes-and-layout.md)
- [Secrets](../secrets.md)
:::
