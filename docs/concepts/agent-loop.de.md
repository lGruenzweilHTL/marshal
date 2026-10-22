---
icon: fa-solid fa-rotate
description: "Wie Marshal einen Turn oder einen Aufgabenlauf ausführt: die Schleife aus Modell- und Werkzeugaufrufen, ihre Grenzen und wie sie sich verhält, wenn etwas schiefgeht."
---

# Agentenschleife

Diese Seite erklärt, wie Marshal einen Turn oder einen Aufgabenlauf ausführt: die Schleife aus Modell- und Werkzeugaufrufen, ihre Grenzen und wie sie sich verhält, wenn etwas schiefgeht.

## Das Problem

Ein LLM, das Werkzeuge aufrufen kann, braucht einen Treiber. Der Treiber entscheidet, welche Werkzeuge das Modell sieht, führt die angeforderten Aufrufe aus, gibt die Ergebnisse zurück und hört im richtigen Moment auf. Fertige Treiber führen Werkzeugaufrufe oft automatisch im Modell-Client aus. Das macht es schwer, jeden Aufruf vor der Ausführung zu prüfen, aufzuzeichnen, was passiert ist, und ein Modell zu stoppen, das sich im Kreis dreht.

Sprache bringt ein zweites Problem. Wer in der Küche steht, wartet nicht 30 Sekunden auf eine Antwort.

## Wie es funktioniert

Jeder Turn und jeder Aufgabenlauf nutzt dieselbe Schleife:

1. **Kontext aufbauen.** Der [Kontextaufbau](context-building.md) setzt den System-Prompt zusammen: Grundregeln, Benutzerprofil, Gedächtnis, Zeit und Gebietsschema, Satellitenkontext, Skill-Index, Bereichszustände und Ausgaberegeln.
2. **Das Modell aufrufen**, mit der Unterhaltung und den aktuell sichtbaren Werkzeugen. Der [Sichtbarkeitsfilter](tool-visibility.md) berechnet die Werkzeugliste vor jedem Modellaufruf neu.
3. **Jeden angeforderten Aufruf prüfen.** Jeder Aufruf geht durch den Guard, der `safe` (ausführen), `confirm` (einfrieren und fragen) oder Verweigern entscheidet. Siehe [Risiko und Bestätigung](risk-and-confirmation.md).
4. **Erlaubte Aufrufe ausführen** und ihre Ergebnisse anhängen. Ergebnisse von Werkzeugen mit nicht vertrauenswürdiger Ausgabe werden als Daten eingepackt und belasten den Durchlauf. Siehe [Nicht vertrauenswürdige Inhalte](untrusted-content.md).
5. **Wiederholen**, bis das Modell Text ohne Werkzeugaufrufe liefert oder eine Grenze erreicht ist.

Marshal lässt den Modell-Client nie selbst Werkzeuge aufrufen. Jeder Aufruf geht durch den Guard und wird als Schritt des Durchlaufs aufgezeichnet.

Ist ein Aufruf als ausstehende Aktion eingefroren, bekommt das Modell in diesem Turn keine weiteren Werkzeuge. Ein zweiter Aufruf in derselben Runde bekommt den Fehler `confirmation.already_pending`. Die Aufgabe des Modells ist jetzt, die Bestätigungsfrage zu stellen.

### Modellstufe

Das Agent-Template des Durchlaufs kann eine Stufe nennen. Ohne eine nutzen Sprach-Turns die Stufe `fast` und Text-Turns die Stufe `standard`. Gibt es kein Modell dieser Stufe mit Werkzeugunterstützung, endet der Turn mit einer festen Antwort „Modell nicht verfügbar“. Siehe [`models`](../configuration/marshal-yaml/models.md).

### Reasoning

Ein Modell mit `reasoning` auf `low`, `medium` oder `high` wird in Text-Turns und Aufgabenläufen im Hintergrund gebeten, mit diesem Aufwand zu überlegen. Sprach-Turns fragen wegen der Latenzgrenze für Sprache nie nach Reasoning. Im Web-Chat streamt das Reasoning als Ereignisse `reasoning`, und jeder Werkzeugaufruf als Ereignis `tool` mit einer kurzen Zusammenfassung von Argumenten und Ergebnis. Ergebnisse sensibler Werkzeuge wie Mail und Notizen werden nie zusammengefasst. Siehe [Konversations-API](../api/conversation.md).

### Werkzeugfehler sind Ergebnisse

Ein Werkzeug, das auf erwartbare Weise scheitert, liefert ein fehlgeschlagenes `Result` mit einem stabilen Code, zum Beispiel `printer.offline`. Die Schleife gibt dem Modell einen strukturierten Fehler (`{"error": {"code": ..., "message": ...}}`) und macht weiter. Ein Werkzeug, das eine Ausnahme wirft, wird genauso behandelt, mit dem Code `tool.failed`. Keins von beiden beendet den Turn. Das Modell kann das Problem erklären oder etwas anderes versuchen.

### Grenzen

| Grenze | Standard | Wirkung, wenn erreicht |
|---|---|---|
| Schritte (`agent.maxSteps` oder `maxSteps` des Templates) | 8 Werkzeugrunden | Kein weiteres Werkzeug läuft; der Turn endet mit einer kurzen Antwort, dass die Anfrage nicht fertig wurde. |
| Latenzgrenze für Sprache (`agent.voiceLatencyCap`) | 8 s | Der Turn endet mit einer Antwort „ich arbeite daran“ und läuft als Hintergrundaufgabe weiter. |
| Latenzgrenze für Text (`agent.textLatencyCap`) | 60 s | Wie bei Sprache. |
| Budget der Aufgabe (Tokens, Kosten, Schritte, Laufzeit) | aus der Aufgabe | Der Durchlauf stoppt, und die Aufgabe scheitert mit `budget_exceeded`. |

Die Schlüssel stehen bei [`agent`](../configuration/marshal-yaml/agent.md) und [`tasks`](../configuration/marshal-yaml/tasks.md).

### In den Hintergrund wechseln

Erreicht ein interaktiver Turn seine Latenzgrenze, spricht Marshal eine kurze Antwort „ich arbeite daran und sage Bescheid, wenn es fertig ist“ und beendet den Turn. Der Durchlauf läuft als Hintergrundaufgabe desselben Benutzers weiter, ab seiner letzten abgeschlossenen Werkzeugrunde. Der ursprüngliche Durchlauf wird mit dem Ergebnis `backgrounded` aufgezeichnet. Die Aufgabe liefert ihr Ergebnis später per Sprache an den auslösenden Satelliten (Sprach-Turns) oder an die Weboberfläche (Text-Turns). Die Aufgabe merkt sich die Sitzung, aus der sie kam, ihr Ergebnis erscheint also auch als neue Antwort in dieser Unterhaltung und zählt als Verlauf für den nächsten Turn der Sitzung.

Das Modell kann dasselbe absichtlich mit `agent.move_to_background(statusMessage)` tun, das nur in interaktiven Turns sichtbar ist.

Nach dem Wechsel wird die Werkzeugliste mit den Regeln für den Hintergrund neu berechnet: Der Advisor wird sichtbar, und Aufrufe der Bestätigungsstufe gehen in die Freigabe-Warteschlange statt in eine gesprochene Frage. `denyTools` und `denyConfirm` des auslösenden Satelliten gelten weiter.

### Der Advisor

`agent.ask_advisor(question)` schickt nur die Frage und eine vom Agenten geschriebene Zusammenfassung an ein Modell der Stufe `advisor`, ohne Werkzeuge, und liefert den Rat als Text zurück. Er ist pro Durchlauf begrenzt (standardmäßig 2, `agent.advisor.maxCallsPerRun`, pro Template überschreibbar) und durch eine tägliche Kostengrenze für den Advisor (`agent.advisor.dailyCostCap`, standardmäßig 1). In Sprach-Turns ist er verborgen, ebenso wenn kein Advisor-Modell konfiguriert ist und sobald eine der Grenzen erreicht ist.

### Subagenten

`agent.delegate(template, task)` führt eine verschachtelte Schleife mit Prompt, Werkzeugen, Skills und Stufe eines anderen Templates aus und liefert deren letzten Text als Werkzeugergebnis. Die verschachtelte Schleife nutzt denselben Guard, den Eigentümer, die Satellitenrichtlinie und die Belastung des Aufrufers und gibt ihre Belastung an den Aufrufer zurück. Subagenten führen nie Aktionen der Bestätigungsstufe aus: Sie haben niemanden, den sie fragen können. Die Tiefe der Delegation ist begrenzt (standardmäßig 3, `agent.maxDelegationDepth`). Siehe [Agent-Templates](../guides/agent-templates.md).

### Ausführung ohne Anfrage

Die Schleife braucht keine HTTP-Anfrage. Eigentümer, Quelle, Eingabemodus, Satellit, Sprache und Belastung kommen aus einem expliziten Ausführungskontext. Aufgabenläufe, Freigaben und Tests nutzen alle dieselbe Schleife mit demselben Guard. Deshalb können Tests die Schleife auch gegen einen Fake-`IChatClient` laufen lassen.

## Was aufgezeichnet wird

Jeder Durchlauf wird mit seinen Schritten in Reihenfolge aufgezeichnet: der Kontextaufbau, jeder Modellaufruf (Modell, Stufe, Tokens, Latenz, Zeit bis zum ersten Token, Kosten, Reasoning), jeder Werkzeugaufruf (Werkzeug-ID, Argumente, Ergebnis oder Verweis, Risikostufe, Entscheidung des Guards, Dauer, Fehler) und die Antwort (Text, `continueConversation`). Die Detailansicht eines Laufs in der Weboberfläche zeigt das. Reasoning, das im selben Durchlauf auf ein sensibles Werkzeugergebnis folgt, wird nicht gespeichert. Was wie lange aufbewahrt wird, steht unter [Observability](../guides/observability.md) und [Datenschutz](privacy.md).

## Abwägungen

- **Stufe `fast` für Sprache.** Sprach-Turns nutzen standardmäßig das günstigste, schnellste Modell. Komplexe Sprachanfragen können die Latenzgrenze erreichen und in den Hintergrund wechseln, statt an Ort und Stelle eine langsamere, bessere Antwort zu bekommen.
- **Schrittlimit statt Cleverness.** Ein festes Schrittlimit stoppt außer Kontrolle geratene Schleifen, schneidet aber manchmal eine lange, berechtigte Kette ab. Templates können es erhöhen.
- **Keine Werkzeuge nach dem Einfrieren.** Werkzeuge nach einer ausstehenden Bestätigung zu stoppen hält die Unterhaltung einfach und die eingefrorene Aktion eindeutig, aber das Modell kann im selben Turn keine andere Arbeit erledigen.

## Verwandte Seiten

::: cards
- [Sichtbarkeit von Werkzeugen](tool-visibility.md)
- [Risiko und Bestätigung](risk-and-confirmation.md)
- [Kontextaufbau](context-building.md)
- [Konversations-API](../api/conversation.md)
:::
