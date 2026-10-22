---
icon: fa-solid fa-layer-group
description: "Die drei Risikostufen, wie Marshal die Stufe eines Werkzeugaufrufs bestimmt, und die Gründe, Status und Entscheidungen, die es aufzeichnet."
---

# Risikostufen

Die drei Risikostufen, wie Marshal die Stufe eines Werkzeugaufrufs bestimmt, und die Gründe, Status und Entscheidungen, die es aufzeichnet.

## Stufen

Jeder Werkzeugaufruf hat genau eine Stufe.

| Stufe | Verhalten |
|---|---|
| :badge[safe]{tip} | Das Werkzeug läuft sofort. |
| :badge[confirm]{warning} | Das Werkzeug läuft erst, nachdem eine Benutzeraktion den eingefrorenen Aufruf bestätigt hat. |
| :badge[forbidden]{caution} | Das Werkzeug läuft nie. Marshal entfernt es aus der Werkzeugliste, und der Guard lehnt einen Aufruf ab. |

Fähigkeiten, die es nie geben darf (Mail senden, roher G-Code), sind überhaupt keine Werkzeuge, daher gilt für sie keine Stufe. Die Standardstufe jedes Werkzeugs steht unter [Werkzeuge](tools.md).

## Wie die Stufe bestimmt wird

1. **Regeln.** Marshal wertet die Regeln aus [`risk.yaml`](../configuration/risk-yaml.md) der Reihe nach aus. Die erste passende Regel setzt die Stufe. Eine Regel passt auf einen Glob der Werkzeug-ID, optionale Bedingungen an Argumente und optionale Bedingungen an Ziele (`domain`, `deviceClass`, `area`).
2. **Standard.** Passt keine Regel, nutzen eingebaute Werkzeuge die Standardstufe ihres Deskriptors. Externe Werkzeuge (MCP-Server) stehen standardmäßig auf `confirm`.
3. **Ziele.** Bei Regeln mit Zielbedingungen erweitert Marshal den Aufruf zu seinen konkreten Zielen und wertet die Regeln pro Ziel aus. Die höchste Stufe gewinnt. Ein Aufruf, dessen Ziele sich nicht auflösen lassen, zu nichts aufgelöst werden oder zu einem Namensraum ohne Resolver gehören, während eine Zielregel passt, ist `confirm` mit dem Grund `unresolved_target`.
4. **Bestätigende Argumente.** Manche Argumente heben einen Aufruf mindestens auf `confirm`, wenn sie gesetzt sind. `overrideQuietHoursUntil` bei `tasks.create`, `tasks.watch` und `tasks.update` ist eines davon: Der Grund ist `quiet_hours_override`.
5. **Belastung.** In einem belasteten Durchlauf hebt Marshal Aufrufe von Werkzeugen mit der Markierung `persists` von `safe` auf `confirm`, mit dem Grund `taint`. Der Guard hebt Stufen nur an, er senkt sie nie.
6. **Sichtbarkeit.** Vor jedem Modellaufruf verbirgt Marshal diese Werkzeuge: Werkzeuge in der Stufe `forbidden`, Werkzeuge außerhalb der Allowlist des Templates, für den Satelliten verbotene Werkzeuge (`denyTools`), Werkzeuge der Bestätigungsstufe auf einem Satelliten, der für sie keine Bestätigung erlaubt (`denyConfirm`), Werkzeuge der Bestätigungsstufe in einem Subagenten und Werkzeuge, die ihre eigene Bedingung verbirgt. Werkzeuge, deren Stufe von den Argumenten abhängt, bleiben sichtbar. Der Guard wiederholt diese Prüfungen bei jedem Aufruf.

> [!NOTE]
> Eine Regel kann die Stufe eines eingebauten Werkzeugs senken, zum Beispiel von `confirm` auf `safe`. Marshal senkt eine Stufe nie von selbst, und die Anhebung durch Belastung gilt zusätzlich zum Ergebnis.

## Ablauf der Bestätigung

Ein Aufruf in der Stufe `confirm` in einem interaktiven Turn wird zu einer ausstehenden Aktion.

1. Marshal speichert Werkzeug, Argumente, Sitzung, Satellit, Eigentümer, eine Nonce, die Gründe und einen Ablaufzeitpunkt. Es beendet den Turn mit `continueConversation = true` und fragt den Benutzer.
2. Eine Sitzung hat höchstens eine ausstehende Aktion. Eine neue ersetzt die alte.
3. Beim nächsten Turn des Benutzers normalisiert Marshal die ganze Äußerung (Leerraum entfernen, Kleinbuchstaben, Satzzeichen entfernen) und vergleicht sie mit den Bestätigungsschlüsselwörtern der Sprache der Anfrage. Ein exakter Treffer führt die eingefrorene Aktion ohne Modellaufruf aus.
4. Sonst liest ein Prüfer der Stufe `fast` nur die Zusammenfassung der Aktion und die Antwort und antwortet `positive`, `neutral` oder `negative`.
   - `positive` führt die eingefrorene Aktion aus.
   - `neutral` verwirft die Aktion und behandelt die Nachricht als neuen Turn.
   - `negative` verwirft die Aktion und antwortet, dass sie abgebrochen wurde.
   - Eine Zeitüberschreitung des Prüfers oder eine nicht lesbare Antwort verwirft die Aktion.
5. Das Modell kann eine eingefrorene Aktion nicht ändern. Nach dem Ablauf (`confirmation.ttl`) läuft nichts mehr, und die Aktion ist `expired`.

Nur eine Benutzeraktion bestätigt: ein Turn des Benutzers in derselben Sitzung, eine Freigabe in der Weboberfläche oder eine Push-Aktion mit geprüfter Nonce. Werkzeugausgaben, Modellausgaben und andere Inhalte bestätigen nie. Subagenten führen nie Aktionen der Bestätigungsstufe aus. In Hintergrundaufgaben geht ein Aufruf der Bestätigungsstufe in die Freigabe-Warteschlange (siehe [Freigaben](../guides/tasks/approvals.md)).

Schlüsselwörter, TTL und Zeitlimit des Prüfers stellst du unter [`confirmation`](../configuration/marshal-yaml/confirmation.md) ein.

## Gründe für eine Bestätigung

Jede ausstehende Aktion trägt einen oder mehrere Gründe, jeweils mit einem Code und einem kurzen Satz in der Sprache der Anfrage. Das Werkzeugergebnis nennt dem Modell den Satz, und der Systemprompt verlangt, dass die Frage ihn enthält. Gelten mehrere Gründe, wird `taint` gesprochen.

| Code | Bedeutung |
|---|---|
| `rule` | Eine Regel oder die Standardstufe des Werkzeugs verlangt eine Bestätigung. |
| `unresolved_target` | Die Ziele des Aufrufs ließen sich nicht auflösen oder wurden zu nichts aufgelöst, während eine Zielregel gilt. |
| `taint` | Der Durchlauf hat nicht vertrauenswürdige Inhalte gelesen, und das Werkzeug speichert Zustand. Der Satz nennt die Quelle, zum Beispiel Mail. |
| `quiet_hours_override` | Der Aufruf setzt `overrideQuietHoursUntil`. |

## Entscheidungen des Guards

Der Guard zeichnet bei jedem Werkzeugaufruf-Schritt eines Durchlaufs eine Entscheidung auf.

| Entscheidung | Bedeutung |
|---|---|
| `safe` | Der Aufruf war sicher und lief. |
| `pending` | Der Aufruf wurde zu einer ausstehenden Aktion. |
| `confirmed_by_keyword` | Ein Bestätigungsschlüsselwort hat gepasst. |
| `judge_positive` | Der Prüfer hat die Bestätigung angenommen. |
| `judge_neutral` | Der Prüfer hat einen Themenwechsel erkannt und die Aktion verworfen. |
| `judge_negative` | Der Prüfer hat eine Ablehnung erkannt und die Aktion verworfen. |
| `escalated_by_taint` | Ein Werkzeug mit `persists` wurde in einem belasteten Durchlauf auf `confirm` gehoben. |
| `denied` | Der Guard hat den Aufruf mit Grund abgelehnt. Das Modell bekommt ein Fehlerergebnis. |
| `expired` | Der Benutzer hat nach dem Ablauf bestätigt. |
| `confirmed_in_ui` | Der Benutzer hat die Aktion in Marshals eigenem Dialog bestätigt, zum Beispiel mit dem Abbrechen-Knopf des Drucker-Widgets im Chat. |

Ein Werkzeugaufruf, den eine MCP-App anfordert, wird nie bestätigt: Ein Aufruf der Bestätigungsstufe aus einer App ist `denied` mit dem Fehlercode `app_confirm`, und es entsteht keine ausstehende Aktion. Ein Klick in HTML, das der Server erstellt hat, zählt nicht als deine Bestätigung.

Eine ausstehende Aktion endet selbst als bestätigt, `expired`, `superseded` oder verworfen. Diese Entscheidungen erscheinen als Tag `decision` der Metrik `marshal.tool.calls`, siehe [Metriken und Traces](metrics-and-traces.md).

## Belastung

Ein Durchlauf wird belastet, wenn das Ergebnis eines als nicht vertrauenswürdig markierten Werkzeugs in seinen Kontext gelangt. Er zeichnet die Quellen der Belastung auf (Art und ersten belastenden Schritt) und löscht sie innerhalb des Durchlaufs nie. Auch ein Durchlauf, den ein Ereignis startet, ist belastet, mit der Quelle `event`, außer die Ereignisquelle gilt im Code als vertrauenswürdig.

| Quelle der Belastung | Gesetzt durch |
|---|---|
| `mail` | `mail.search`, `mail.read_message`, `mail.read_thread` |
| `rss` | `rss.get_unread`, `rss.get_article`, `rss.search`, `rss.fetch_full_text` |
| `notes` | `notes.*` |
| `web` | `web.search` und `web.answer` mit dem Antworttyp `text` |
| `calendar` | `calendar.list_events` |
| `media` | `media.*` |
| `music` | `music.search`, `music.queue` |
| `github` | `repos.*` |
| `artifact` | `artifacts.list`, `artifacts.inspect` |
| `notifications` | `notifications.inbox_list` |
| `mcp:<server id>` | Werkzeuge eines nicht vertrauenswürdigen MCP-Servers oder Treffer von `untrustedTools` |
| `event` | Ein Durchlauf, den ein nicht vertrauenswürdiges Ereignis gestartet hat |

Werkzeuge, die Zustand speichern und in einem belasteten Durchlauf angehoben werden: `memory.remember`, die `tasks.*`-Werkzeuge, die Arbeit speichern (`create`, `schedule_wakeup`, `watch`, `update`, `run_executor`), und `mail.save_attachment`.

## Verwandte Seiten

::: cards
- [Werkzeuge](tools.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [Risikoregeln](../guides/risk-rules.md)
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
:::
