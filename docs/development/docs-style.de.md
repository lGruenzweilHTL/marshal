---
icon: fa-solid fa-pen-nib
description: "Wie du die Seiten in docs/ schreibst und pflegst."
---

# Stil der Dokumentation

Wie du die Seiten in `docs/` schreibst und pflegst. Der Stil des Codes steht in [STYLE_GUIDE.md](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md).

## Maßgebliche Quelle

Die Spezifikationen in `openspec/specs/` legen fest, wie Marshal sich verhält. Die Dokumentation beschreibt dieses Verhalten für Menschen; sie erfindet nie Verhalten, das die Spezifikationen nicht haben.

- Schreib nach der Spezifikation und prüfe dann den Code. Widersprechen sie sich, gewinnt die Spezifikation: Dokumentiere ihr Verhalten und melde die Abweichung, damit der Code (oder die Spezifikation) korrigiert wird.
- Standardwerte, Schlüsselnamen und Validierungsmeldungen der Konfiguration kommen aus den Options-Klassen und Validatoren des Codes, abgeglichen mit der Spezifikation und `deploy/config.example/`.
- Dokumentiere keine Interna, die ein Benutzer oder Mitwirkender weder beobachten noch beeinflussen kann.

## Aufbau

Die Doku folgt [Diátaxis](https://diataxis.fr/). Jede Seite hat eine Aufgabe:

| Ordner | Art | Beantwortet |
|---|---|---|
| `getting-started/` | Tutorial | „Führ mich einmal hindurch.“ Ein Weg, keine Auswahl, endet mit etwas, das funktioniert. |
| `installation/`, `guides/`, `home-assistant-component/` | Anleitung | „Wie mache ich X?“ Beginnt mit einem Ziel, nennt die Voraussetzungen, endet, wenn das Ziel erreicht ist. |
| `configuration/`, `reference/`, `api/` | Referenz | „Was genau macht X?“ Vollständig, knapp, strukturiert, ohne Erzählung. |
| `concepts/` | Erklärung | „Warum funktioniert es so?“ Hintergrund, Abwägungen, Designregeln. |
| `development/` | Anleitung und Referenz für Mitwirkende | „Wie ändere ich Marshal?“ |

Misch keine Arten auf einer Seite. Eine Anleitung verlinkt für die vollständige Liste der Schlüssel auf die Referenz, statt sie zu wiederholen; eine Referenzseite verlinkt für den Ablauf auf die Anleitung.

## Ton

- Schreib das Original auf Englisch, in schlichter, direkter Prosa. Sprich den Leser mit „you“ an. Deutsche Übersetzungen folgen [Übersetzungen](#übersetzungen).
- Aktiv, Präsens: „Marshal hides the tool“, nicht „the tool will be hidden“.
- Eine Idee pro Satz. Halte die meisten Sätze unter 25 Wörtern.
- Nutze einen Begriff für eine Sache, wie im [Glossar](../reference/glossary.md) festgelegt. Wechsle nicht zwischen „confirm tier“ und „confirmation level“.
- Anweisungen stehen im Imperativ: „Set `agent.maxSteps`“, nicht „`agent.maxSteps` should be set“.
- Keine Werbesprache, keine Füllwörter („simply“, „just“, „easily“), keine Emoji im Fließtext.
- Sag klar, was nicht unterstützt wird, und warum, wenn der Grund wichtig ist (zum Beispiel, warum Mail keinen Sendepfad hat).

## Struktur einer Seite

Jede Seite beginnt mit einer Überschrift der Ebene 1 und einer Zusammenfassung in ein oder zwei Sätzen, was die Seite abdeckt. Dann:

**Anleitungen** (How-to):

```markdown
# Connect a Moonraker printer

One sentence on the outcome.

## Before you start
- prerequisites, versions, what you need at hand

## Steps
### 1. ...
### 2. ...

## Check that it works

## Troubleshooting   (optional, short; link to guides/troubleshooting.md for more)

## Related
- links to configuration and concept pages
```

Seiten der **Konfigurationsreferenz** beschreiben eine Datei oder einen Abschnitt der obersten Ebene:

````markdown
# `agent`

One sentence on what the section controls.

## Example
```yaml
...minimal, valid example...
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `agent.maxSteps` | integer | `8` | ... |

## Validation
- what startup rejects, with the exact error text where useful

## Related
````

Dauern nutzen das .NET-Format `TimeSpan` wie in der Beispielkonfiguration (`"00:00:08"`, `"30.00:00:00"`); sag das in der Typspalte (`duration`).

Seiten mit **Konzepten** dürfen jede Struktur nutzen, beginnen aber mit dem Problem, das das Design löst.

## Konventionen für Markdown

Die Seiten sind in [Clay Flavored Markdown](https://clay-doc.github.io/clay-example-repo/writing/markdown.html) (CFM) geschrieben: GitHub-flavored Markdown plus Alerts und Komponenten. [Clay](https://clay-doc.github.io/clay-example-repo/) baut daraus die Website der Dokumentation (siehe [Die Website](#die-website)). Komponenten bleiben als reiner Text lesbar, jede Seite liest sich also auch auf GitHub gut.

- Überschriften: ein `#` pro Seite, dann `##` und `###`. Überspring keine Ebenen. Im Englischen Satzschreibung („Before you start“).
- Links zwischen Seiten sind relativ und enthalten die Endung `.md`: `[risk tiers](../reference/risk-tiers.md)`. Verlinke eine Überschrift mit `#anchor`. Verlinke Dateien außerhalb von `docs/` (`CONTRIBUTING.md`, `STYLE_GUIDE.md`) über ihre vollständige GitHub-URL; die Website enthält sie nicht.
- Hinweise nutzen GitHub-Alerts, sparsam:
  - `> [!NOTE]` zusätzlicher Kontext
  - `> [!TIP]` ein besserer Weg
  - `> [!IMPORTANT]` nötig für den Erfolg
  - `> [!WARNING]` Risiko von Datenverlust, Auswirkung auf die Sicherheit oder eine kaputte Einrichtung
- Codeblöcke nennen immer eine Sprache (`yaml`, `sh`, `json`, `http`, `csharp`, `python`, `text`). Nenne die Datei in eckigen Klammern, wenn der Block eine Datei oder ein Teil davon ist: ```` ```yaml [/config/marshal.yaml] ````.
- Shell-Beispiele nutzen `sh` und kein Prompt-Zeichen. Ersetze Werte, die der Leser ändern muss, durch `<spitze-klammern>`.
- YAML-Beispiele müssen gültig sein und zum echten Schema passen. Schreib nie einen Secret-Wert in ein YAML-Beispiel; verweise auf einen Secret-Namen, wie `/config` es verlangt.
- Tabellen für Schlüssel, Flags, Ereignisse und Werkzeuge. Prosa für alles andere.
- Bilder liegen in `docs/images/` und brauchen einen aussagekräftigen Alt-Text. Diagramme sind `mermaid`-Codeblöcke in der Seite, die sie erklärt, nie Bilder (siehe [Die Diagramme pflegen](../diagrams/README.md)).
- Dateinamen: Kleinbuchstaben, mit Bindestrichen getrennt, `.md`.

### Komponenten

Nutze eine Komponente, wo sie zum Inhalt passt, nicht als Dekoration:

| Inhalt | Komponente |
|---|---|
| Die nummerierten Schritte einer Anleitung (`## Steps`) | `::: steps`, jeder Schritt beginnt fett mit dem, was zu tun ist |
| Alternativen, von denen der Leser eine wählt (Option A oder B, Container oder Quellbaum) | `::: tabs` mit Zeilen `== Label` |
| Dasselbe Beispiel in mehreren Sprachen oder Werkzeugen (curl und Python) | `::: code-group` mit der Beschriftung in eckigen Klammern nach jeder Sprache |
| FAQ-Einträge, lange Logs, Sonderfälle | `::: details <question>` |
| `## Related`, „wohin als Nächstes“ und Listen von Seiten | `::: cards`, ein Link pro Zeile; füg nur dann eine Textzeile unter einem Link hinzu, wenn der Grund für den Link nicht die eigene Beschreibung der Seite ist |
| Ein Aufruf einer Operation der API v1 | `::: request v1 <operationId>`, mit Startwerten in YAML (`body:`) |

Verschachtle einen Container in einem anderen, indem du dem äußeren mehr Doppelpunkte gibst (`:::: tabs` … `::::`).

Ein Schritt ist keine Überschrift und hat daher keinen Anker. Verlinken andere Seiten einen Teil einer Anleitung, mach diesen Teil zu einem `###`-Abschnitt statt zu einem Schritt.

### Front Matter

Jede Seite beginnt mit Front Matter:

- `order`, wo die Reihenfolge in der Seitenleiste wichtig ist (Tutorials, Installation), in Zehnerschritten,
- `icon` (eine freie Klasse aus Font Awesome 6 wie `fa-solid fa-key`) auf jeder Seite und jedem Ordner,
- `description` auf jeder Seite: ein Satz reiner Text (kein Markdown), angezeigt auf Karten, die auf die Seite verlinken, und in Suchergebnissen,
- die `index.md` des Ordners: `title`, `icon`, `order` und `description`. Verlinke unter ihrer Zusammenfassung jede Seite und jeden Unterordner des Ordners mit Karten, gruppiert unter `##`-Überschriften, wenn der Ordner groß ist. Nutze kein `list: true`.

## Übersetzungen

Die Website gibt es auf Englisch und Deutsch (`languages` in `clay.yaml`). Englisch ist das Original. Eine deutsche Seite liegt neben ihrem Original, mit `.de` vor `.md`: `docs/guides/memory.de.md` übersetzt `docs/guides/memory.md`, und `index.de.md` übersetzt die Seite eines Ordners. Eine Seite ohne Übersetzung zeigt in der deutschen Navigation den englischen Text, mit einem Hinweis.

- Eine Änderung an einer englischen Seite aktualisiert ihre deutsche Übersetzung im selben Commit. Kannst du sie nicht übersetzen, lösch die deutsche Datei, statt sie veralten zu lassen: Der englische Rückfall ist besser als eine falsche Übersetzung.
- Beginne eine neue Übersetzung mit `npx clay-doc@5 edit translate <page> --lang de`. Der Befehl kopiert das Original; übersetze dann den Text.
- Übersetze `title` und `description` im Front Matter, die Überschriften und den Text. Behalte `icon` und `order`; die Position in der Navigation kommt immer aus dem Original.
- Lass Codeblöcke, Konfigurationsschlüssel, Werkzeug-IDs, Dateinamen, Umgebungsvariablen, Fehlermeldungen und Log-Zeilen auf Englisch, genau so, wie Marshal sie schreibt. Übersetze Kommentare in Beispielen nur, wenn sie dem Leser das Beispiel erklären.
- Schreib Links genau wie im Original. Anker (`#...`) entstehen aus den deutschen Überschriften, ein Link auf einen Abschnitt einer übersetzten Seite nutzt also den deutschen Anker.
- Sprich den Leser mit „du“ an, wie die deutsche Weboberfläche. Es gelten dieselben Regeln für den Ton wie im Englischen: aktiv, Präsens, kurze Sätze, keine Füllwörter.
- Nutze die Begriffe der deutschen Weboberfläche (`web/src/*/messages.ts`):

| Englisch | Deutsch |
|---|---|
| tool | Werkzeug |
| run | Durchlauf |
| task | Aufgabe |
| memory, memory chunk | Gedächtnis, Gedächtniseintrag |
| risk tier, confirm tier | Risikostufe, Bestätigungsstufe (die Werte `safe`, `confirm`, `forbidden` bleiben wie geschrieben) |
| guard | Guard |
| tainted, taint | belastet, Belastung |
| untrusted content | nicht vertrauenswürdige Inhalte |
| approval, approval queue | Freigabe, Freigabe-Warteschlange |
| notification, inbox | Benachrichtigung, Posteingang |
| delivery | Zustellung |
| trigger | Auslöser |
| owner | Eigentümer |
| satellite | Satellit |
| skill, agent template | Skill, Agent-Template |
| secret | Secret |
| artifact | Artefakt |
| kill switch | Not-Aus |
| quiet hours | Ruhezeiten |
| turn | Turn (ein Sprach- oder Chat-Turn) |

## Die Website

`clay.yaml` im Wurzelverzeichnis des Repositorys konfiguriert die Website; der [Workflow Deploy Docs](https://github.com/lGruenzweilHTL/marshal/blob/master/.github/workflows/docs.yaml) baut sie mit `npx clay-doc@5 build --strict` und veröffentlicht sie auf GitHub Pages, wenn sich auf `master` `docs/`, `clay.yaml` oder das OpenAPI-Dokument ändern.

Prüfe die Doku, bevor du pushst. Das dauert ein paar Sekunden und findet kaputte Links, Anker und Anfragezellen:

```sh
npx clay-doc@5 check
```

Sieh dir die Website mit Live-Reload unter `http://localhost:3000` an:

```sh
npx clay-doc@5 dev
```

Die [API-v1-Referenz](../api/v1/index.md) wird erzeugt: `scripts/docs-openapi.mjs` läuft vor jedem Clay-Befehl, kopiert `web/openapi/v1.json` nach `artifacts/openapi/` und ergänzt die Server. Bearbeite die erzeugten Seiten nicht; ändere die API und führe `npm run gen:api` in `web/` aus.

## Die Doku aktuell halten

- Eine Änderung, die einen Konfigurationsschlüssel, ein Werkzeug, ein Ereignis, einen CLI-Befehl, einen Endpunkt oder eine Integration hinzufügt, entfernt oder umbenennt, aktualisiert die passende Referenzseite im selben Commit.
- Eine neue Integration bekommt eine Seite in `guides/integrations/` und Zeilen in `reference/tools.md`, nach [Eine Integration hinzufügen](adding-an-integration.md).
- Fällt dir eine Seite auf, die der Spezifikation widerspricht, korrigiere die Seite oder den Code, wenn der Code falsch ist.
