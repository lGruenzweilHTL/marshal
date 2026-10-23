---
icon: fa-solid fa-hand
description: "Wie Marshal entscheidet, ob ein Werkzeugaufruf läuft, deine Bestätigung braucht oder nie läuft, und warum bestätigte Aktionen auf dem Server eingefroren werden."
---

# Risiko und Bestätigung

Diese Seite erklärt, wie Marshal entscheidet, ob ein Werkzeugaufruf läuft, deine Bestätigung braucht oder nie läuft, und warum bestätigte Aktionen auf dem Server eingefroren werden.

## Das Problem

Ein Agent, der Türen aufsperren, Drucke starten und Erinnerungen anlegen kann, darf nicht jeder Idee des Modells folgen. Nach einer Bestätigung zu fragen hilft, aber eine naive Bestätigung hat Lücken:

- Das Modell fragt „soll ich die Haustür aufsperren?“, du sagst ja, und das Modell ruft das Werkzeug dann mit anderen Argumenten auf.
- Eine Mail oder Webseite enthält das Wort „bestätigt“, und das Modell hält es für deine Antwort.
- Eine Bestätigungsfrage bleibt eine Stunde lang offen, und ein verirrtes „ja“ führt sie später aus.
- Eine Regel, die Schlösser schützt, wird umgangen, indem „alles im Flur“ ausgeschaltet wird, was ein Schloss einschließt.

## Risikostufen

Jeder Werkzeugaufruf hat genau eine Stufe:

| Stufe | Bedeutung |
|---|---|
| :badge[safe]{tip} | Sofort ausführen. |
| :badge[confirm]{warning} | Nur ausführen, nachdem eine Benutzeraktion bestätigt hat. |
| :badge[forbidden]{caution} | Nie ausführen. |

Fähigkeiten, die nie passieren dürfen, etwa Mail senden oder roher G-Code, sind überhaupt keine Werkzeuge. Eine Stufe könnte eine Regel ändern; ein fehlendes Werkzeug nicht. Siehe [Risikostufen](../reference/risk-tiers.md).

## Regeln

Regeln stehen in `/config/risk.yaml`. Sie treffen Werkzeug-IDs per Glob, optional mit Bedingungen an Argumente oder Ziele (`domain`, `deviceClass`), der Reihe nach; der erste Treffer gewinnt. Werkzeuge ohne passende Regel nutzen die Standardstufe ihres Deskriptors. Externe Werkzeuge ohne passende Regel stehen standardmäßig auf `confirm`. Die Regeln gelten gleich für eingebaute und externe Werkzeuge und werden ohne Neustart neu geladen. Siehe [`risk.yaml`](../configuration/risk-yaml.md) und [Risikoregeln](../guides/risk-rules.md).

### Ziele scheitern sicher

Regeln mit Zielbedingungen werden auf die konkreten Ziele angewendet, zu denen ein Aufruf aufgelöst wird, nicht auf seine Argumente, wie sie geschrieben sind. Für Home Assistant erweitert ein Resolver `name`, `area`, `floor`, `domain` und `device_class` zu den freigegebenen Entitäten. Jedes Ziel wird geprüft, und die höchste Stufe gewinnt, „schalte den Flur aus“ ist also `confirm`, wenn im Flur ein Schloss ist.

Ein Aufruf, dessen Ziele sich nicht auflösen lassen, zu nichts aufgelöst werden oder zu einem Namensraum ohne Resolver gehören, während eine Zielregel passt, ist `confirm` mit dem Grund `unresolved_target`. Ist Marshal unsicher, fragt es.

### Belastung hebt Stufen nur an

Hat ein Durchlauf nicht vertrauenswürdige Inhalte gelesen (Mail, RSS, Notizen, Kalender, nicht vertrauenswürdige MCP-Werkzeuge, Nutzdaten von Ereignissen), werden Aufrufe von Werkzeugen mit `persists` von `safe` auf `confirm` mit dem Grund `taint` angehoben. Siehe [Nicht vertrauenswürdige Inhalte](untrusted-content.md). Keine Stufe des Guards senkt je eine Stufe.

## Eingefrorene ausstehende Aktionen

Einfrieren eines Aufrufs der Bestätigungsstufe:

<!--@include: ../_diagrams/confirm-tier-freeze.de.md-->

Bestätigen:

<!--@include: ../_diagrams/confirm-tier-confirm.de.md-->

Wird ein Aufruf in einem interaktiven Turn zu `confirm`, bittet Marshal das Modell nicht, sich daran zu erinnern. Es speichert eine **ausstehende Aktion** auf dem Server, bevor irgendjemand gefragt wird:

- Werkzeug und genaue Argumente
- Sitzung, Satellit und Eigentümer
- eine Nonce
- die Gründe
- einen Ablauf (standardmäßig 60 Sekunden, `confirmation.ttl`)

Das Werkzeugergebnis sagt dem Modell, dass eine Bestätigung aussteht und die Aktion nicht ausgeführt wurde, und gibt den Begründungssatz, den die Frage enthalten muss. Der Turn endet mit `continueConversation = true`, ein Sprachsatellit hört also weiter zu.

Bei Bestätigung führt Marshal genau das gespeicherte Werkzeug mit den gespeicherten Argumenten aus. Das Modell kann sie nicht ändern. Vor der Ausführung prüft Marshal die Regeln erneut; ein eingefrorener Aufruf, dessen Werkzeug weg ist oder jetzt `forbidden` ist, läuft nicht.

Pro Sitzung gibt es höchstens eine ausstehende Aktion. Ein neuer Aufruf der Bestätigungsstufe löst den alten ab, der als `superseded` markiert wird. Schließt eine Sitzung im Leerlauf oder ruht eine Web-Chat-Sitzung im Leerlauf, wird ihre ausstehende Aktion verworfen.

## Wie du bestätigst

Nur Benutzeraktionen bestätigen:

- **Ein Benutzer-Turn in derselben Sitzung.** Das ist der normale Weg bei Sprache.
- **Eine Freigabe in der Weboberfläche.**
- **Eine Push-Aktion mit geprüfter Nonce** aus der Home-Assistant-Companion-App.

Werkzeugausgabe, Modellausgabe und Inhalte jeder Art bestätigen nie. Enthält ein Werkzeugergebnis „bestätigt“, während eine Aktion aussteht, bleibt die Aktion ausstehend.

### Erst Schlüsselwort, dann Prüfer

Beim nächsten Benutzer-Turn normalisiert Marshal die ganze Äußerung (trimmen, Kleinschreibung, Satzzeichen entfernen) und vergleicht sie mit den Bestätigungswörtern für die Sprache der Anfrage. Die eingebauten Listen sind `confirm`, `confirmed`, `approved`, `yes do it` (Englisch) und `bestätigt`, `bestätigen`, `ja mach`, `ja mach das` (Deutsch); du kannst sie pro Sprache mit `confirmation.keywords` ersetzen. Ein genauer Treffer führt die eingefrorene Aktion ohne Modellaufruf aus. „Bestätigt!“ trifft; „bestätigt, aber erst morgen“ nicht.

Ohne Treffer bekommt ein Prüfer der Stufe `fast` nur die Zusammenfassung der Aktion und deine Antwort, bei Temperatur 0, und antwortet genau eins von:

| Urteil | Wirkung |
|---|---|
| `positive` | Die eingefrorene Aktion wird ausgeführt. |
| `neutral` | Die Aktion wird verworfen, und deine Nachricht wird als normaler neuer Turn behandelt. |
| `negative` | Die Aktion wird verworfen, und Marshal antwortet, dass sie abgebrochen wurde. |

Der Prüfer soll `neutral` antworten, wenn er nicht sicher ist. Eine Zeitüberschreitung des Prüfers (standardmäßig 3 Sekunden, `confirmation.judgeTimeout`) oder eine nicht lesbare Ausgabe verwirft die Aktion. Eine Bestätigung nach dem Ablauf führt nichts aus; die Aktion wird als `expired` markiert, und die Antwort sagt das.

Siehe [`confirmation`](../configuration/marshal-yaml/confirmation.md).

## Gründe

Jede ausstehende Aktion trägt einen oder mehrere Gründe, jeweils mit Code und einem kurzen Satz in der Sprache der Anfrage:

| Code | Wann |
|---|---|
| `rule` | Eine Risikoregel hat den Aufruf zu `confirm` gemacht. |
| `unresolved_target` | Die Ziele ließen sich nicht auflösen. |
| `taint` | Der Durchlauf hat nicht vertrauenswürdige Inhalte gelesen, und das Werkzeug speichert Zustand. Der Satz nennt die Quelle, zum Beispiel dass die Unterhaltung Mail-Inhalte enthielt, die versteckte Anweisungen enthalten können. |
| `quiet_hours_override` | Eine Aufgabe oder Beobachtung würde dich während der Ruhezeiten benachrichtigen. |

Der Server schreibt diese Sätze, die Erklärung gibt es also auch, wenn das Modell sie weglässt. Gelten mehrere Gründe, wird `taint` gesprochen.

## Durchläufe im Hintergrund

Ein Aufgabenlauf hat im Moment niemanden, den er fragen kann. Ein Aufruf der Bestätigungsstufe erzeugt dort dieselbe Art eingefrorener Aktion, mit einer TTL von 10 Minuten (`tasks.approvalTtl`), und schickt sie mit einem Push mit Aktionen in die Freigabe-Warteschlange. Der Durchlauf endet, statt einen Worker zu blockieren. Siehe [Aufgaben und Zeitplanung](tasks-and-scheduling.md) und [Freigaben](../guides/tasks/approvals.md).

Ein [AI-Task](../home-assistant-component/ai-tasks.md) ist auch nicht interaktiv, hat aber keine Aufgabe, die fortgesetzt werden kann. Seine Aufrufe der Bestätigungsstufe und durch Belastung angehobene `persists`-Aufrufe gehen genauso in die Freigabe-Warteschlange, und der Durchlauf läuft mit einem Werkzeugergebnis weiter, das sagt, dass die Aktion wartet. Eine spätere Freigabe führt genau den eingefrorenen Aufruf aus und schickt eine Benachrichtigung `low` mit dem Ergebnis. Diese Freigaben liegen nie als ausstehende Aktion in der Sitzung, keine spätere Eingabe in dieser Sitzung kann sie also bestätigen.

## Satelliten und Subagenten

Auf einem Satelliten mit `denyConfirm` für ein Werkzeug wird ein Aufruf, der zu `confirm` wird, mit „not allowed from this device“ verweigert, und keine ausstehende Aktion entsteht. Der Aufruf eines Subagenten in der Bestätigungsstufe wird mit einer Meldung verweigert, die ihm sagt, die nötige Aktion an seinen Aufrufer zu melden.

## Aufgezeichnete Entscheidungen

Jede Entscheidung des Guards wird im Schritt des Durchlaufs aufgezeichnet: `safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired`. Die Detailansicht eines Laufs zeigt, warum jeder Aufruf lief oder nicht.

## Abwägungen

- **Kurze TTL.** Sechzig Sekunden sind absichtlich kurz: Eine Bestätigung gehört zu der Unterhaltung, die sie ausgelöst hat. Manchmal musst du noch einmal fragen.
- **Ein Prüfer, der `neutral` bevorzugt.** Marshal verwirft lieber eine Aktion und behandelt deine Antwort als neue Anfrage, als auf ein missverstandenes „klar, aber später“ hin auszuführen.
- **Sicheres Scheitern bei Zielen.** Unbekannte Gerätenamen fragen nach einer Bestätigung, was Reibung erzeugt, wenn eine Entität nicht für Assist freigegeben ist.

## Verwandte Seiten

::: cards
- [Sichtbarkeit von Werkzeugen](tool-visibility.md)
- [Nicht vertrauenswürdige Inhalte](untrusted-content.md)
- [Sicherheitsmodell](security-model.md)
- [Rückfragen](../guides/voice/follow-ups.md)
:::
