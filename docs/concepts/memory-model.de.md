---
icon: fa-solid fa-brain
description: "Wie Marshal sich über Unterhaltungen hinweg an Dinge erinnert: was ein Gedächtniseintrag ist, wie Einträge einen Turn erreichen, wie Projekte sie eingrenzen und wie das Gedächtnis geschrieben wird."
---

# Gedächtnismodell

Diese Seite erklärt, wie Marshal sich über Unterhaltungen hinweg an Dinge erinnert: was ein Gedächtniseintrag ist, wie Einträge einen Turn erreichen, wie Projekte sie eingrenzen und wie das Gedächtnis geschrieben wird.

## Das Problem

Ein Assistent ohne Gedächtnis stellt jeden Tag dieselben Fragen. Ein Assistent mit naivem Gedächtnis hat andere Probleme:

- Er speichert Beinahe-Duplikate und verschmilzt Fakten, die nur ähnlich aussehen, sodass aus „Düse 0,4“ und „Düse 0,6“ ein Durcheinander wird.
- Er verlässt sich darauf, dass das Modell ein Werkzeug „Gedächtnis durchsuchen“ aufruft, was kleine Modelle vergessen und was in jedem Sprach-Turn einen Schritt kostet.
- Er füllt den Prompt mit allem, was er weiß, oder mit dem, was ein Angreifer in eine zusammengefasste Mail geschmuggelt hat.
- Er kann dir nicht sagen, was er weiß, woher das kommt oder was er früher geglaubt hat.

## Einträge

Das Gedächtnis ist eine Menge typisierter **Einträge** (Chunks). Jeder Eintrag hat:

- einen Eigentümer, einen Geltungsbereich (`general` oder `project`) und eine Projekt-ID (nur beim Geltungsbereich Projekt gesetzt)
- einen Typ: `fact`, `preference`, `event`, `procedure`, `person` oder `device`
- einen Titel und Inhalt, immer auf Englisch geschrieben, egal welche Sprache du sprichst
- eine Wichtigkeit von 1 bis 5 und eine Markierung als angeheftet
- eine optionale `source_ref` (zum Beispiel einen Notizpfad)
- ein Embedding, Zeitstempel und einen optionalen Löschzeitpunkt (Soft Delete)

### Deterministische IDs

Die ID eines Eintrags ist ein Hash aus Eigentümer, Geltungsbereich, Projekt, Typ und normalisiertem Titel. Wird derselbe Titel noch einmal geschrieben, trifft das denselben Eintrag und aktualisiert ihn an Ort und Stelle. Einträge werden nie nach Ähnlichkeit verschmolzen. Zwei Eigentümer mit demselben Titel bekommen verschiedene IDs. Ein Projekt umzubenennen ändert die IDs der Einträge nicht.

### Versionen und Kanten

Eine Aktualisierung eines Eintrags behält den vorherigen Inhalt, die Wichtigkeit und die Quelle als Versionszeile, „Voron nozzle size“ zeigt also 0,6 mm und sein Verlauf 0,4 mm. Eine Änderung der Identität (Titel, Geltungsbereich oder Projekt) legt einen neuen Eintrag mit einer Kante `supersedes` zum alten an.

Einträge und Versionen tragen auch eine Gültigkeitszeit (`validFrom`, optional `validTo`): wann der Fakt in der Welt galt, getrennt davon, wann Marshal ihn gespeichert hat. Eine Aktualisierung oder Ablösung beendet die Gültigkeit des vorherigen Fakts. Der automatische Abruf nutzt nur jetzt gültige Fakten, beendete Fakten kosten also keinen Platz im Prompt; `memory.recall` mit `asOf` beantwortet Fragen nach „vorher“ und „wann“ aus geschlossenen Einträgen und früheren Versionen. Die ID enthält die Gültigkeitszeit nicht, ein Fakt behält also über seine Lebensdauer seine Identität.

Einträge sind über vier Arten von Kanten verbunden: `supersedes`, `contradicts`, `depends_on` und `relates_to`. Einen Eintrag zu löschen löscht ihn weich: Er wird nicht mehr abgerufen, bleibt aber in der Verlaufsansicht sichtbar.

Die Tabellen hinter dem Gedächtnis mit ihren Spalten und Beziehungen stehen im [Datenmodell](../diagrams/erd.md#gedächtnis).

### Speicherung

Einträge liegen in SQLite, indiziert in FTS5 (Titel und Inhalt) und in `sqlite-vec` (Embedding). Embeddings kommen vom konfigurierten mehrsprachigen Embedding-Modell; siehe [`embedding`](../configuration/marshal-yaml/embedding.md). Ändert sich der Inhalt, ändern sich Embedding und FTS-Eintrag in derselben Transaktion.

## Wie das Gedächtnis einen Turn erreicht

Der Abruf ist automatisch und läuft auf dem Server. Das Modell muss kein Werkzeug aufrufen.

1. **Angeheftete Ebene.** Angeheftete Einträge werden immer vollständig eingefügt, vor allem anderen, unabhängig von der Relevanz.
2. **Embedding des Turns.** Marshal berechnet einmal das Embedding des Turn-Texts mit dem Bereichsnamen. Dasselbe Embedding wird für die Projektaktivierung wiederverwendet.
3. **Startpunkte.** Vektorähnlichkeit und FTS-Treffer auf Schlüsselwörter liefern beide Startpunkte (standardmäßig je 20). Ein Schlüsselworttreffer zählt, wenn das Embedding einen genauen Gerätenamen verfehlt.
4. **Erweiterung.** Der Abruf folgt den Kanten in die Breite, ausgehend von den Startpunkten. Abgelöste Einträge werden durch ihre Nachfolger ersetzt.
5. **Bewertung.** Jeder Kandidat bekommt Ähnlichkeit × Wichtigkeit × Frische. Die Frische nimmt mit dem Alter seit der letzten Aktualisierung ab, mit einer Halbwertszeit pro Typ (30 Tage für Ereignisse, ein Jahr für Fakten und Präferenzen, zwei Jahre für Personen, Abläufe und Geräte).
6. **Kontingente und Budget.** Die Ergebnisse halten Kontingente pro Typ ein (zum Beispiel höchstens 5 Ereignisse) und ein Token-Budget (standardmäßig 600 Tokens, angeheftete Einträge kommen dazu).

Das Ergebnis erscheint im System-Prompt als „Relevant memory“, mit dem Hinweis, dass es veraltet sein kann und dass gilt, was du jetzt sagst. Fallen der Embedding-Server oder der Index aus, läuft der Turn ohne abgerufenes Gedächtnis weiter; die Gerätesteuerung hängt nie davon ab.

Für tiefere Suchen ruft das Modell `memory.recall(query, type?, project?, limit?)` auf, das ein größeres Budget hat (standardmäßig 2000 Tokens).

Der Schritt Kontextaufbau des Durchlaufs zeichnet auf, welche Einträge genutzt wurden und warum, die Detailansicht eines Laufs in der Weboberfläche zeigt also, was Marshal in jedem Turn wusste.

## Projekte

Projekte grenzen das Gedächtnis weich ein. Ein Eintrag eines aktiven Projekts wird verstärkt (standardmäßig × 1,5); ein Eintrag eines inaktiven Projekts wird abgewertet (× 0,5), nie ausgeschlossen. Ist der Eintrag eines inaktiven Projekts der einzige starke Treffer, erscheint er trotzdem.

Ein Projekt wird auf eine dieser Arten aktiv:

| Art | Wie |
|---|---|
| Explizit | Der Turn-Text enthält den Namen des Projekts oder einen aktiven Alias. Kein Modellaufruf. Grund `alias`. |
| Erschlossen | Das Embedding des Turns wird mit dem Schwerpunkt des Projekts verglichen (Beschreibung plus seine besten Einträge, abzüglich abgelehnter Formulierungen), plus einer Vorgewichtung nach Aktualität. Hohe Sicherheit (≥ 0,75) aktiviert dauerhaft, mittlere (≥ 0,6) nur für diesen Turn, niedrige tut nichts. |
| Dauerhaft | Eine dauerhafte Aktivierung gilt für spätere Turns weiter, wird mit jedem Turn schwächer und fällt bei einem Themenwechsel weg. |

Nach einer erschlossenen Aktivierung wird per Sprache nie gefragt. Stattdessen korrigierst du sie: „nicht dieses Projekt“ lässt das Modell `memory.correct_project` mit `rejected` aufrufen, was das Projekt entlädt und die Formulierung als Gegenbeispiel speichert. Formulierungen, die zu unkorrigierten Schlüssen geführt haben, werden Alias-Kandidaten und nach 3 Bestätigungen zu aktiven Aliassen befördert.

Aktive Projekte und ihre Gründe werden im Durchlauf aufgezeichnet und in der Weboberfläche gezeigt, nie gesprochen.

## Wie das Gedächtnis geschrieben wird

Das Gedächtnis wird auf drei Arten geschrieben, alle über die deterministische ID:

- **Explizit.** `memory.remember(type, title, content, importance?, scope?, project?, sourceRef?)` schreibt oder aktualisiert einen Eintrag. Es kann nicht anheften; Anheften ist eine Benutzeraktion in der Weboberfläche. In belasteten Durchläufen braucht es eine Bestätigung mit dem Grund `taint`.
- **Beim Schließen oder Ruhen einer Sitzung.** Schließt eine Sitzung nach ihrer Leerlauf-TTL oder ruht eine Web-Chat-Sitzung, schlägt ein günstiges Modell Kandidaten für Einträge vor, nur aus deinen Turns und den Antworten von Marshal, nie aus rohen Werkzeugergebnissen. Ergebnisse aus dem Hintergrund zählen als Antworten der Sitzung. Jede Zusammenfassung liest nur die Turns seit der letzten, eine Web-Sitzung, die mehrmals ruht, wird also nicht erneut von Anfang an zusammengefasst. Ein Kandidat, der einem bestehenden Eintrag widerspricht, legt eine Kante `contradicts` an, statt ihn zu überschreiben.
- **Nächtliche Konsolidierung.** Eine Systemaufgabe lässt dieselbe Zusammenfassung über die geschlossenen Sitzungen des Tages laufen.

Notizen sind nie von selbst Gedächtnis. Inhalte aus Notizen kommen nur ins Gedächtnis, wenn der Agent einen Eintrag in eigenen Worten schreibt, mit `source_ref` auf den Notizpfad.

## Das Gedächtnis gesund halten

Eine Prüfung listet veraltete Einträge (standardmäßig 180 Tage weder aktualisiert noch abgerufen), offene Widersprüche und Waisen (Einträge gelöschter Projekte, hängende Kanten). Nach der nächtlichen Konsolidierung machen Jobs in Leerlaufzeiten aus offenen Widersprüchen und Beinahe-Duplikaten Vorschläge und schreiben Projektzusammenfassungen; ein Vorschlag ändert das Gedächtnis erst, wenn du ihn annimmst. Die Gedächtnisansicht der Weboberfläche zeigt Einträge, Versionen, Kanten und die Prüfung. Jede Nacht schreibt die Sicherung einen NDJSON-Export der Einträge, Versionen und Kanten jedes Eigentümers (ohne Embeddings), den du wieder importieren kannst; die Embeddings werden neu berechnet.

Zum täglichen Umgang mit dem Gedächtnis siehe [Gedächtnis](../guides/memory.md).

## Abwägungen

- **Englische Einträge.** Alle Einträge auf Englisch zu schreiben hält Titel für deterministische IDs stabil und funktioniert mit einem mehrsprachigen Embedding-Modell, auf Kosten deiner genauen Worte.
- **Kein Verschmelzen nach Ähnlichkeit.** Deterministische IDs bedeuten, dass zwei Formulierungen desselben Fakts nebeneinander bestehen können. Die Prüfung und der Blick der Zusammenfassung auf das bestehende Gedächtnis verringern das, beseitigen es aber nicht.
- **Der automatische Abruf kostet in jedem Turn Prompt-Tokens.** Token-Budget und Kontingente begrenzen das.
- **Ein Wechsel des Embedding-Modells braucht einen Neustart und berechnet die Embeddings des Gedächtnisses neu.**

## Verwandte Seiten

::: cards
- [Kontextaufbau](context-building.md)
- [Nicht vertrauenswürdige Inhalte](untrusted-content.md)
- [Datenbesitz](data-ownership.md)
- [Sicherungen und Wiederherstellung](../guides/backups-and-restore.md)
:::
