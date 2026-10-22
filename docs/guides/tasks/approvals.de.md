---
icon: fa-solid fa-circle-check
description: "Prüfe und entscheide die Aktionen, die Hintergrundaufgaben und Automationen nicht ohne dich ausführen dürfen, und beantworte Eingabeanfragen von MCP-Servern."
---

# Freigaben

Prüfe und entscheide die Aktionen, die Hintergrundaufgaben und Automationen nicht ohne dich ausführen dürfen, und beantworte Eingabeanfragen von MCP-Servern.

## Bevor du anfängst

- Push-Benachrichtigungen funktionieren (Home-Assistant-App), wenn du vom Handy aus entscheiden willst, siehe [Benachrichtigungen](../notifications.md).
- Freigaben im Hintergrund laufen nach `tasks.approvalTtl` ab (Standard 10 Minuten); siehe [`tasks`](../../configuration/marshal-yaml/tasks.md).

## Wie es funktioniert

Ruft ein Durchlauf im Hintergrund ein Werkzeug der Bestätigungsstufe auf, friert Marshal den Aufruf ein (Werkzeug, Argumente, Nonce, Ablauf) und schickt dir einen Push mit Aktionen und der Priorität `normal`. Die Freigabe-Warteschlange in der Weboberfläche zeigt dieselbe Aktion. Der Durchlauf endet mit dem Status `waiting` und einer Notiz in der Aufgabe, die das Werkzeug und eine Zusammenfassung der Argumente nennt, damit kein Worker blockiert ist.

Bei einer Freigabe führt der Server genau die eingefrorene Aktion aus und setzt die Aufgabe mit Nutzdaten fort, die Entscheidung und Ergebnis enthalten. Bei Ablehnung oder Ablauf geht die Aufgabe mit `rejected` weiter (ein Ablauf ergänzt `reason: expired`). Das Modell führt eine freigegebene Aktion nie selbst aus.

Warum du gefragt wirst, steht wörtlich im Push und in der Warteschlange, zum Beispiel:

- `This needs your confirmation: <action>.` (eine Regel in `risk.yaml`, siehe [Risikoregeln](../risk-rules.md))
- `I couldn't tell exactly which device you mean.` (nicht aufgelöstes Ziel)
- `This conversation included content from your mail, and such content can contain hidden instructions.` (Belastung)
- `This would notify you during your quiet hours.` (Ausnahme von den Ruhezeiten)

### Durchläufe ohne Aufgabe

Ein [AI-Task](../../home-assistant-component/ai-tasks.md) aus einer Home-Assistant-Automation hat keine Aufgabe, die weitergehen könnte, und niemanden, der während des Durchlaufs bestätigt. Ein Aufruf der Bestätigungsstufe, oder ein Aufruf, der Gedächtnis oder Aufgaben schreibt, nachdem der Durchlauf nicht vertrauenswürdige Inhalte gelesen hat, geht auf dieselbe Weise in die Freigabe-Warteschlange, mit derselben TTL, demselben Push und denselben Gründen. Der Durchlauf wartet nicht: Er läuft mit einem Werkzeugergebnis weiter, das sagt, dass die Aktion auf eine Freigabe wartet, und seine Daten sagen das auch.

Bei einer Freigabe führt der Server genau die eingefrorene Aktion aus und schickt dir eine `low`-Benachrichtigung mit dem Ergebnis. Bei Ablehnung oder Ablauf läuft nichts. Eine spätere Nachricht in derselben Sitzung bestätigt die Aktion nie.

### Eingabeanfragen

Ein MCP-Server kann dich während eines Werkzeugaufrufs um eine Eingabe bitten, zum Beispiel um ein Formularfeld oder eine Anmeldeseite. Marshal friert den Aufruf ein und zeigt die Anfrage im Eingang (und im Chat eines Web-Turns) als Formular oder als Link mit **Fertig**. Nur die Weboberfläche beantwortet sie; der Push trägt einen Link und keine Schaltflächen. Das Modell sieht die Anfrage und deine Antwort nie. Siehe [Eingabeanfragen](../../configuration/mcp-servers.md#eingabeanfragen).

## Schritte

::: steps
1. **Entscheide in der Weboberfläche**

   Öffne **Eingang**; jede Freigabe ist ein Ticket mit der Aktion, der Aufgabe, den Gründen und einem Link zum Werkzeugschritt, der den Durchlauf belastet hat. Wähle **Freigeben** oder **Ablehnen**.

2. **Entscheide über den Push**

   Tippe in der Benachrichtigung auf **Freigeben** oder **Ablehnen**. Die Aktion trägt eine Nonce, Marshal nimmt nur eine passende Nonce einer ausstehenden Freigabe im Hintergrund an, und alles andere wird ignoriert. Eingabeanfragen haben keine Push-Aktionen; tippe auf die Benachrichtigung, um die Anfrage in der Weboberfläche zu öffnen.

3. **Entscheide per Stimme**

   Eine Bestätigung kann auch als Rückfrage per Stimme in der Unterhaltung kommen (Schlüsselwort oder Prüfer, siehe [Risikoregeln](../risk-rules.md)). Nur eine Benutzeraktion bestätigt: Werkzeugausgaben, Modellausgaben oder sonstige Inhalte tun es nie.
:::

## Fehler, die du sehen kannst

| Meldung | Bedeutung |
|---|---|
| `The approval expired; the action was not executed.` | Die TTL ist abgelaufen. Bitte die Aufgabe, es erneut zu versuchen. |
| `The approval was already decided.` | Jemand (oder der Push) hat zuerst entschieden. |
| `The task no longer waits for this approval; the action was not executed.` | Die Aufgabe wurde abgebrochen oder ist beendet. |
| `The task is still finishing its run; try again in a moment.` | Warte ein paar Sekunden und versuch es noch einmal. |

## Prüfen, ob es funktioniert

Bitte um eine Aufgabe, die eine Bestätigung braucht, zum Beispiel eine Beobachtung, die nach dem Lesen von Mail angelegt wird. Das Ticket erscheint im **Eingang** und auf deinem Handy. Gib es frei, und der nächste Durchlauf der Aufgabe zeigt `approved` und das Ergebnis in seinen Auslöser-Nutzdaten.

## Verwandte Seiten

::: cards
- [Risikoregeln](../risk-rules.md)
- [Geplante Aufgaben](scheduled-tasks.md)
- [AI-Tasks](../../home-assistant-component/ai-tasks.md)
- [MCP-Server](../../configuration/mcp-servers.md)
- [Risiko und Bestätigung](../../concepts/risk-and-confirmation.md)
- [Risikostufen](../../reference/risk-tiers.md)
:::
