---
icon: fa-solid fa-brain
description: "Bring Marshal Fakten und Vorlieben bei, sag, wann sie galten, prüfe, was es sich merkt, und verwalte Projekte, Anheften, die Prüfung und seine Vorschläge."
---

# Gedächtnis

Bring Marshal Fakten und Vorlieben bei, sag, wann sie galten, prüfe, was es sich merkt, und verwalte Projekte, Anheften, die Prüfung und seine Vorschläge.

## Bevor du anfängst

- Ein mehrsprachiges Embedding-Modell ist konfiguriert, siehe [`embedding`](../configuration/marshal-yaml/embedding.md). Ein Wechsel bettet beim nächsten Start das ganze Gedächtnis neu ein.
- Konzepte: [Gedächtnismodell](../concepts/memory-model.md).

## Was Marshal sich merkt

Das Gedächtnis ist eine Menge von Einträgen. Jeder hat eine Art (`fact`, `preference`, `event`, `procedure`, `person`, `device`), einen Titel, einen Inhalt, eine Wichtigkeit von 1 bis 5, einen Bereich (`general` oder `project`), eine Gültigkeitszeit und einen Verlauf von Versionen. Titel und Inhalt sind auf Englisch geschrieben. Die ID eines Eintrags ist ein Hash aus Eigentümer, Bereich, Projekt, Art und normalisiertem Titel. Wer denselben Titel erneut schreibt, aktualisiert also denselben Eintrag und behält den vorherigen Inhalt als Version. Einträge werden nie nach Ähnlichkeit zusammengeführt.

## Dein Gedächtnis nutzen

### Marshal etwas merken lassen

```text
Merk dir, dass der Voron eine 0,6-mm-Düse hat.
```

Der Agent ruft `memory.remember` auf (Art, Titel, Inhalt, optional Wichtigkeit, Bereich, Projekt, sourceRef). In einem sauberen Durchlauf schreibt er ohne Rückfrage. In einem belasteten Durchlauf (er hat Mail, RSS, Notizen, Webinhalte oder Ereignisdaten gelesen) braucht das Schreiben deine Bestätigung mit dem Grund `taint`, siehe [Freigaben](tasks/approvals.md). `memory.remember` kann nicht anheften.

Marshal lernt auch von selbst: Wenn eine Sitzung endet oder eine Web-Chat-Sitzung nach `sessions.idleTtl` ruht, schlägt ein Zusammenfasser der Stufe fast Einträge vor, und zwar nur aus deinen Turns und den Antworten von Marshal, nie aus rohen Werkzeugergebnissen. Ein Kandidat, der einem gespeicherten Eintrag widerspricht, erzeugt eine Kante `contradicts` und lässt den gespeicherten Eintrag unverändert. Eine nächtliche Konsolidierung erfasst Sitzungen, die der Abschlussweg verpasst hat.

### Sagen, seit wann oder bis wann

Jeder Eintrag und jede Version hat eine Gültigkeitszeit: `validFrom` und optional `validTo`, die Zeitspanne, in der der Fakt in der Welt galt. Das ist unabhängig davon, wann Marshal ihn gespeichert hat.

```text
Merk dir, dass ich seit März 2025 bei Fronius arbeite.
```

Der Agent übergibt `validFrom` und `validTo` an `memory.remember` als Datum (`2025-03-01`) oder als ISO-8601-Zeitpunkt. Beide werden in deiner Zeitzone gelesen; ein Datum bedeutet 00:00. Ohne `validFrom` gilt der Eintrag ab dem Moment, in dem er geschrieben wird. Ein `validTo` vor `validFrom` wird abgelehnt. Der Zusammenfasser setzt eine Gültigkeitszeit nur, wenn deine Turns eine nennen.

Marshal schließt Gültigkeitszeiten in zwei Fällen selbst:

- Beim Aktualisieren eines Eintrags an Ort und Stelle: Der vorherige Inhalt wird eine Version, die endet, wenn der neue Inhalt beginnt.
- Beim Ersetzen eines Eintrags: Der alte Eintrag endet, wenn sein Nachfolger beginnt.

Ein Eintrag, dessen `validTo` vorbei ist, ist geschlossen. Geschlossene Einträge bleiben im Gedächtnis und im Verlauf, aber die automatische Suche lässt sie aus.

### Sehen, was in einem Turn verwendet wird

Die Suche läuft automatisch: Für jeden Turn bettet Marshal den Text ein, sucht nach Vektor und Stichwort (FTS), erweitert entlang der Kanten, ersetzt abgelöste Einträge durch ihre Nachfolger und bewertet jeden Kandidaten als Ähnlichkeit x Wichtigkeit x Frische. Kontingente pro Art und ein Token-Budget gelten. Angeheftete Einträge kommen immer vollständig in den Kontext, vor den gefundenen. Öffne **Läufe**, wähle einen Durchlauf und lies seinen Kontextschritt, um zu sehen, welche Einträge und Projekte verwendet wurden.

Für eine gezielte, tiefe Suche ruft der Agent `memory.recall` auf (Anfrage, optional Art, Projekt, Limit), mit einem größeren Token-Budget. Ohne `asOf` sucht es in Einträgen, die jetzt gelten. Mit `asOf` (Datum oder Zeitpunkt) sucht es, was damals galt, einschließlich geschlossener Einträge und früherer Versionen, und jedes Ergebnis trägt seine Gültigkeitszeit:

```text
Welche Düse hatte der Voron im August?
```

Für aktive Projekte bekommt der Turn außerdem die Zusammenfassung des Projekts (siehe [Projekte nutzen](#projekte-nutzen)).

### In der Weboberfläche durchsehen und bearbeiten

**Gedächtnis** hat diese Reiter:

| Reiter | Zweck |
|---|---|
| Einträge | Suchen und filtern (der Filter **Nicht mehr gültig** listet geschlossene Einträge), den Editor öffnen, Inhalt, Wichtigkeit und Gültigkeitszeit bearbeiten, den Verlauf ansehen (Versionen und Ersetzungsketten mit ihren Gültigkeitszeiten), anheften oder lösen, löschen (weiches Löschen). |
| Projekte | Beschreibungen, Alias-Namen, Alias-Kandidaten, das Korrekturprotokoll und die nur lesbare Projektzusammenfassung von Marshal. |
| Karte | Wechsel zwischen einem kräftebasierten Graphen der Einträge und Kanten und einem PCA-Streudiagramm der Embeddings. Auf einem Handy zeigt die Karte stattdessen eine Liste. |
| Prüfung | Vorschläge zum Annehmen oder Verwerfen, veraltete Einträge, offene Widersprüche und verwaiste Einträge. |

Anheften geht nur hier (oder über die API); der Agent kann nicht anheften. Weich gelöschte Einträge sind von der Suche ausgeschlossen, bleiben aber in der Verlaufsansicht. Um einen Eintrag mit geänderter Identität zu ersetzen, nutzt du Ersetzen: Es legt einen neuen Eintrag mit einer Kante `supersedes` an.

Im Editor setzen **Gültig seit** und **Gültig bis** die Gültigkeitszeit. Lass **Gültig bis** leer, solange der Fakt noch gilt; wenn du das Feld leerst, öffnest du einen geschlossenen Eintrag wieder. Die Felder nutzen die Zeitzone deines Browsers.

### Die Prüfung abarbeiten

Die Prüfung listet Einträge, die seit `memory.audit.staleAfterDays` (Standard 180) nicht aktualisiert oder gefunden wurden, Widerspruchspaare (bis einer den anderen ersetzt, geschlossen oder gelöscht wird) und verwaiste Einträge (Einträge gelöschter Projekte, hängende Kanten). Öffne jeden Punkt und bearbeite, ersetze oder lösche ihn.

### Vorschläge annehmen oder verwerfen

Nach der nächtlichen Konsolidierung sehen sich Leerlauf-Jobs das Gedächtnis an, während niemand mit Marshal spricht. Sie nutzen die Stufe `fast`, lesen nur Einträge und Projektbeschreibungen (nie Sitzungen oder Werkzeugergebnisse) und zählen gegen die tägliche Kostengrenze der Hintergrundaufgaben. Fällt ein Job aus, laufen die anderen weiter.

| Job | Was er tut |
|---|---|
| Widerspruchsvorschläge | Schlägt für jedes offene Paar `contradicts` vor, welcher Fakt noch gilt und seit wann der andere veraltet ist. Jedes Paar wird einmal vorgeschlagen. |
| Duplikatvorschläge | Findet für Einträge, die sich seit der letzten Nacht geändert haben, Einträge derselben Art und desselben Bereichs, deren Embeddings mindestens `memory.duplicateThreshold` (Standard 0.92) ähnlich sind, und schlägt einen zusammengeführten Text vor. |
| Projektzusammenfassungen | Schreibt eine Zusammenfassung von höchstens 120 Wörtern für jedes Projekt, dessen Einträge sich geändert haben. |

Vorschläge erscheinen oben in **Gedächtnis → Prüfung**. Nichts ändert sich, bis du entscheidest:

- **Annehmen** bei einem Widerspruch schließt den veralteten Eintrag zum vorgeschlagenen Zeitpunkt.
- **Annehmen** bei einem Duplikat aktualisiert den behaltenen Eintrag mit dem zusammengeführten Text und ersetzt den anderen.
- **Verwerfen** lässt das Gedächtnis unverändert. Ein verworfenes Paar wird nicht noch einmal vorgeschlagen.

Das Annehmen wird als deine Aktion in den Versionen des Eintrags und im Prüfprotokoll festgehalten. Die API bietet dasselbe über `GET /api/v1/memory/proposals` und `POST /api/v1/memory/proposals/{id}/accept` oder `/dismiss`.

### Projekte nutzen

Ein Projekt gruppiert Einträge (Bereich `project`). Marshal aktiviert ein Projekt:

- Über Name oder Alias im Text des Turns, ohne Modellaufruf (Grund `alias`).
- Über die Ähnlichkeit des Turns zum Zentroid des Projekts (Beschreibung plus wichtigste Einträge, abzüglich abgelehnter Formulierungen). Hohe Sicherheit aktiviert dauerhaft, mittlere nur für diesen Turn, niedrige tut nichts. Dauerhafte Aktivierungen klingen mit jedem Turn ab und fallen bei einem Themenwechsel weg.

Einträge aktiver Projekte werden bevorzugt und Einträge inaktiver Projekte abgewertet, nie ausgeschlossen. Wählt Marshal das falsche Projekt, sag es („nicht dieses Projekt“): Der Agent ruft `memory.correct_project` mit `rejected`, `switched` oder `confirmed` auf. Jede Korrektur wird mit ihrer Formulierung gespeichert. Formulierungen, die zu einer nicht korrigierten Zuordnung geführt haben, werden Alias-Kandidaten und nach 3 Bestätigungen zu Alias-Namen (`memory.projects.aliasPromotion`). Die aktiven Projekte pro Turn und der Grund stehen im Detail des Durchlaufs, sie werden nie gesprochen.

Hat ein aktives Projekt eine Zusammenfassung, fügt Marshal sie dem Kontext des Turns hinzu. **Gedächtnis → Projekte** zeigt die Zusammenfassung und wann Marshal sie geschrieben hat; du kannst sie nicht bearbeiten, weil der nächste nächtliche Lauf sie überschreiben würde. Ein Projekt ohne Zusammenfassung fügt nichts hinzu.

## Abstimmen

Die Standardwerte sind Ausgangspunkte: Token-Budget der Suche `memory.retrieval.tokenBudget` (600), Kontingente und Halbwertszeiten pro Art, Schwellen für die Projektsicherheit (`highConfidence` 0.75, `mediumConfidence` 0.6), der Zusammenfasser `memory.summarizer.enabled` und `maxTurns` und die Duplikatschwelle `memory.duplicateThreshold` (0.92). Alle Schlüssel stehen in [`embedding` und `memory`](../configuration/marshal-yaml/embedding.md).

## Export und Backup

Das nächtliche Backup schreibt neben die Datenbankkopie einen NDJSON-Export der Einträge, Versionen und Kanten jedes Eigentümers mit ihren Gültigkeitszeiten (ohne Embeddings), siehe [Backups und Wiederherstellung](backups-and-restore.md). Einen Export lädst du über die API unter `GET /api/v1/memory/export` herunter.

## Prüfen, ob es funktioniert

Sag „merk dir, dass mein Lieblingskaffee ein Flat White mit Hafermilch ist“, öffne **Gedächtnis** und finde den Eintrag, dann frag in einer neuen Sitzung „welchen Kaffee mag ich?“.

## Verwandte Seiten

::: cards
- [Gedächtnismodell](../concepts/memory-model.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
- [Kontextaufbau](../concepts/context-building.md)
- [Datenbesitz](../concepts/data-ownership.md)
:::
