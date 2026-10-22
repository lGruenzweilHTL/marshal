---
icon: fa-solid fa-layer-group
description: "Wie Marshal zu Beginn jedes Durchlaufs zusammensetzt, was das Modell sieht: den System-Prompt, seine Abschnitte und ihre Reihenfolge."
---

# Kontextaufbau

Diese Seite erklärt, wie Marshal zu Beginn jedes Durchlaufs zusammensetzt, was das Modell sieht: den System-Prompt, seine Abschnitte und ihre Reihenfolge.

## Das Problem

Was das Modell weiß, entscheidet, was es tut. Mit zu wenig Kontext stellt es Fragen, die es nicht stellen müsste; mit zu viel ist es langsam, teuer und abgelenkt. Ein Kontext mit allen Entitätszuständen im Haus ist beides. Ein Kontext mit deinem fest eingetragenen Namen bricht für einen zweiten Benutzer. Ein Kontext, der einen Mailtext neben Anweisungen einfügt, lädt die Mail ein, Anweisungen zu geben.

## Wie es funktioniert

Der System-Prompt wird aus **Contributors** gebaut, die jeweils `IContextContributor` implementieren. Jeder Contributor fügt einen Abschnitt hinzu oder nichts und gibt eine **Stabilität** an:

- `static`: gleich für jeden Durchlauf mit derselben Konfiguration.
- `session`: gleich für jeden Turn einer Sitzung.
- `turn`: kann sich mit jedem Turn ändern.

Marshal stellt alle `static`-Abschnitte zuerst, dann `session`, dann `turn`. Innerhalb einer Stabilität entscheidet der Ordnungswert. Die Abschnitte erscheinen in dieser Reihenfolge:

| Stabilität | Ordnung | Contributor | Fügt hinzu |
|---|---|---|---|
| `static` | 0 | Basis-Prompt | Die festen Regeln von Marshal: nur die gegebenen Werkzeuge nutzen, kurz antworten, `<untrusted-data>` als Daten behandeln, bei ausstehender Bestätigung den Begründungssatz mitsagen, Fehler erklären statt endlos neu zu versuchen. |
| `static` | 50 | Template-Prompt | Der Prompt-Text des aktiven Agent-Templates. |
| `static` | 250 | Skill-Index | Name und Beschreibung jedes verfügbaren Skills, nie der Inhalt. |
| `static` | 260 | Delegationsindex | Templates, an die das Modell delegieren kann. |
| `session` | 100 | Benutzerprofil | Dein Anzeigename aus dem Profil des Eigentümers. |
| `session` | 150 | Angeheftetes Gedächtnis | Alle angehefteten Einträge, vollständig. |
| `session` | 300 | Satellitenkontext | Bereich und Stockwerk, `verbosity` und `notes` des Satellitenprofils und der Lautsprecher des Raums. |
| `session` | 400 | Ausgabemodus | Regeln für Sprache oder Text. |
| `turn` | 160 | Abgerufenes Gedächtnis | Für diesen Turn abgerufene Einträge; siehe [Gedächtnismodell](memory-model.md). |
| `turn` | 200 | Zeit und Gebietsschema | Lokales Datum, Wochentag und Uhrzeit in deiner Zeitzone, in der Antwortsprache formatiert, und die Antwortsprache. |
| `turn` | 350 | Bereichszustände | Zustände der freigegebenen Home-Assistant-Entitäten im Bereich des Turns und der Entitäten, die du erwähnt hast. |
| `turn` | 450 | Aufgabenregeln | Nur für Aufgabenläufe: niemand wartet, frühere Durchläufe sind nicht sichtbar, wie das Ergebnis zu formen ist. |

### Stabiler Text zuerst

Modellanbieter cachen den Anfang eines Prompts. Ein gecachtes Präfix ist günstiger und lässt das erste Token früher ankommen, was bei Sprach-Turns am meisten zählt. Weil stabile Abschnitte zuerst kommen, haben zwei Turns einer Sitzung bis zum ersten `turn`-Abschnitt identische System-Prompts. Die Werkzeugliste folgt derselben Idee: Werkzeuge behalten eine feste Reihenfolge, und später geladene Namensräume werden ans Ende angehängt. Siehe [Sichtbarkeit von Werkzeugen](tool-visibility.md#werkzeugsuche).

Auf den System-Prompt folgt der Unterhaltungsverlauf: die letzten Wechsel der Sitzung (deine Nachrichten und die Antworten von Marshal), dann deine neue Nachricht. Bei einer fortgesetzten Aufgabe treten Ziel, Notizen und Nutzdaten des Auslösers an die Stelle eines Transkripts.

### Keine persönlichen Daten im Code

In keinem Prompt steht ein Benutzername oder ein persönliches Detail fest im Code. Der Profil-Contributor liest deinen Namen aus dem Datensatz des Eigentümers, derselbe Code dient also jedem Eigentümer.

### Ausgabemodus

Der Ausgabemodus folgt dem `inputMode` des Turns:

- **Sprache:** schlichter, sprechbarer Text. Kein Markdown. Kurz. Zahlen so geschrieben, wie man sie spricht.
- **Text:** Markdown erlaubt.

### Sprache

Marshal antwortet in der Sprache der Anfrage (`de` oder `en`). Durchläufe ohne Sprache, etwa Aufgabenläufe, nutzen deine gespeicherte Spracheinstellung. System-Prompts bleiben Englisch; Datum und Zahlen werden in der Antwortsprache und deiner Zeitzone formatiert.

### Bereichskontext ohne Komplettauszug

Der Bereichsabschnitt listet nur freigegebene Entitäten im Bereich des Turns und Entitäten, die in deinem Text vorkommen, aus dem Zustandscache. Er enthält nie jeden Zustand im Haus. Braucht das Modell mehr, ruft es `ha.homeassistant__GetLiveContext` auf. Siehe [Home Assistant](../guides/integrations/home-assistant.md).

### Schrittweise Offenlegung von Skills

Skills erscheinen nur als Index. Das Modell lädt den Inhalt eines Skills mit `agent.load_skill(name)`, wenn es ihn braucht, und zusätzliche Dateien mit `agent.load_skill(name, file)`. So bleibt der Prompt klein, auch wenn Dutzende Skills installiert sind. Siehe [Skills](../guides/skills.md).

### Nicht vertrauenswürdige Ergebnisse werden eingepackt

Werkzeugergebnisse aus nicht vertrauenswürdigen Quellen kommen in einer Hülle `<untrusted-data source="...">` in die Unterhaltung, als Daten gekennzeichnet und nie als Anweisungen. Siehe [Nicht vertrauenswürdige Inhalte](untrusted-content.md).

## Transparenz

Der Schritt Kontextaufbau jedes Durchlaufs zeichnet den vollständigen System-Prompt auf, deine Eingabe und Notizen dazu, was aufgenommen wurde: Gedächtniseinträge mit ihren Werten und wie sie gefunden wurden, aktive Projekte mit ihren Gründen und ob das Gedächtnis nicht verfügbar war. Die Detailansicht eines Laufs in der Weboberfläche zeigt diese Notizen. Aktive Projekte werden nie gesprochen.

## Den Kontext erweitern

Eine neue Fähigkeit fügt Kontext hinzu, indem sie einen weiteren Contributor mit Stabilität und Ordnungswert registriert; sie bearbeitet keine bestehenden Prompts. Siehe [Eine Integration hinzufügen](../development/adding-an-integration.md).

## Abwägungen

- **Englische System-Prompts.** Eine Prompt-Sprache hält das Verhalten über Antwortsprachen und Modelle hinweg gleich, auf Kosten eines Übersetzungsschritts, den das Modell implizit macht.
- **Gedächtnis und Bereichskontext automatisch in jedem Turn.** Das spart bei Sprach-Turns eine Werkzeugrunde, kostet aber Prompt-Tokens, auch wenn der Turn sie nicht braucht. Budgets und die Regel „nur der Bereich“ halten das klein.
- **Templates kommen früh.** Der Prompt eines Templates steht direkt nach den Grundregeln, Templates prägen das Verhalten also stark, können die Grundregeln aber nicht entfernen.
- **Stabilität vor Ordnung.** Die Gruppierung nach Stabilität hält das cachebare Präfix lang. Der Preis: Ein `turn`-Abschnitt wie die Uhrzeit kann nie vor einem `static`-Abschnitt stehen, egal welchen Ordnungswert er hat.

## Verwandte Seiten

::: cards
- [Agentenschleife](agent-loop.md)
- [Gedächtnismodell](memory-model.md)
- [Satelliten](../guides/voice/satellites.md)
- [Agent-Templates](../guides/agent-templates.md)
:::
