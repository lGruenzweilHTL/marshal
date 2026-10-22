---
icon: fa-solid fa-stethoscope
description: "Finde die Ursache eines Problems anhand dessen, was du siehst, mit den Meldungen, die Marshal ausgibt."
---

# Fehlerbehebung

Finde die Ursache eines Problems anhand dessen, was du siehst, mit den Meldungen, die Marshal ausgibt.

## Bevor du anfängst

- Lies das Container-Log: `docker compose logs marshal`.
- Prüfe `GET /health` (ohne Authentifizierung) und die Seite **System**. Siehe [Beobachtbarkeit](observability.md).
- Setz `logging.logLevel.default` auf `Debug`, um Stacktraces zu Konfigurationsfehlern zu bekommen.

## Der Start schlägt fehl

Der Start prüft in Stufen (Konfiguration, Datenbank, Secrets, Dienste) und führt eine Stufe nur aus, wenn die vorherigen keine Fehler gefunden haben. Fehler in Konfiguration und Secrets werden je als eine Zeile ausgegeben, ohne Werte, und der Prozess endet mit Code `1`:

```text
marshal: startup failed (2 errors)
  marshal.yaml  embedding.model  is required.
  risk.yaml  risk[1].tier must be safe, confirm or forbidden.
```

| Symptom | Ursache | Lösung |
|---|---|---|
| Fehler in `marshal.yaml` mit Zeile und Spalte, allein gemeldet | Das YAML lässt sich nicht parsen. | Korrigiere die Syntax an dieser Stelle. Nichts anderes wird geprüft, bis es sich parsen lässt. |
| `<key> is required.` / `<model>.provider must be one of ...` / `models.<name>.apiKeySecret is required for anthropic models.` / `...endpoint is required for openai-compatible models.` / `embedding.model is required.` | Fehlende oder ungültige Option. | Setz den Schlüssel aus der Meldung, siehe [`models`](../configuration/marshal-yaml/models.md) und [`embedding`](../configuration/marshal-yaml/embedding.md). |
| Der Fehler nennt eine Umgebungsvariable wie `MARSHAL__Embedding__Provider` | Der ungültige Wert kommt aus der Umgebung, die die Datei überschreibt. | Korrigiere oder entferne die Variable. |
| `'<key>' must hold a secret name (^[a-z][a-z0-9_]{0,63}$), not a value.` | Ein Schlüssel `...Secret` enthält einen Wert oder etwas, das wie einer aussieht. | Leg den Wert in den Secret-Speicher und verweise über den Namen darauf. Eine Hex-Zeichenkette mit 32 Zeichen scheitert auch als Name. |
| `'<key>' names a credential. Store the value as a secret and reference it with a '...Secret' key.` | Ein Schlüssel wie `password`, `token` oder `apiKey` in einer Konfigurationsdatei. | Benenne ihn um in `<thing>Secret: <name>`. `maxTokens` ist in Ordnung. |
| `the value of '<key>' looks like a credential.` | Ein PEM-Header, JWT, `sk-`, `ghp_`, `xox?-` oder ein anderer langer Wert mit hoher Entropie. | Verschieb ihn in ein Secret. Siehe [Secret-Referenz](../configuration/secrets-reference.md). |
| `<reference> does not resolve.` (alle fehlenden Secrets auf einmal gelistet) | Ein nötiges Secret hat keinen Wert in `/run/secrets`, der Umgebung oder der Datenbank. | Leg `/run/secrets/<name>` an oder setz `MARSHAL_SECRET_<NAME>`, siehe [Secrets](secrets.md). |
| `auth.providers allows only one 'local' provider.` / `auth.providers uses the name '<x>' more than once.` | Ungültiger Abschnitt `auth`. | Korrigiere [`auth`](../configuration/marshal-yaml/auth.md). |
| `owner.language is invalid.` | Die Sprache ist nicht `de` oder `en`. | Setz `owner.language`. |
| Einzeilige Zusammenfassung plus Stacktrace | Ein unerwarteter Fehler, zum Beispiel ist der HTTP-Port belegt. | Lies die Zusammenfassung; gib den Port frei oder korrigiere den Mount. |

## Konfigurationsprobleme nach dem Start

| Symptom | Ursache | Lösung |
|---|---|---|
| `risk.yaml` bearbeitet, Regeln unverändert | Das Neuladen hat die Validierung nicht bestanden; die vorherigen Regeln bleiben aktiv. | Lies die Log-Zeile, zum Beispiel `risk.yaml: risk[2].match.tool is required and must be a glob string.`, und korrigiere sie. |
| Ein Werkzeug, das Marshal nutzen soll, fehlt in seiner Werkzeugliste | Eine Regel `forbidden`, eine Allowlist im Template, ein `denyTools` am Satelliten, oder die Integration ist nicht konfiguriert (Werkzeuge bleiben verborgen, bis sie eingerichtet ist). | Prüfe die [Risikoregeln](risk-rules.md), die Liste `tools` des Templates und die Einstellungen der Integration. Verborgene Werkzeuge bekommt das Modell gar nicht zu sehen. |
| Skill wird nicht angeboten; das Log sagt `Skill <name>/SKILL.md rejected: ...` | Ungültiges Frontmatter oder ein Name, der vom Verzeichnis abweicht. | Siehe [Skills](skills.md). Andere Skills laden weiter. |
| Das Log sagt `Template <file> rejected: ...` | Ungültiges Frontmatter des Templates. | Siehe [Agent-Templates](agent-templates.md). |
| `MCP server file <file> rejected: ...` oder `<file>: <key> (<secret>) does not resolve.` | Fehlerhafte MCP-Server-Datei oder fehlendes Secret. | Korrigiere die Datei, siehe [MCP-Server](../configuration/mcp-servers.md). |
| Änderungen an Modellen oder Authentifizierung werden ignoriert | Sie brauchen einen Neustart. | Starte den Container neu. Risikoregeln, Satelliten, Skills, Templates und MCP-Server laden im laufenden Betrieb neu. |

## Bestätigung und Aufgaben

| Symptom | Ursache | Lösung |
|---|---|---|
| Marshal fragt unerwartet nach einer Bestätigung, „this conversation included content from your mail“ | Der Durchlauf ist belastet: Er hat nicht vertrauenswürdige Inhalte gelesen, und ein schreibendes Werkzeug (Gedächtnis, Aufgaben) wurde hochgestuft. | Erwartet. Bestätige, oder frag erneut in einer neuen Unterhaltung, ohne vorher den Inhalt zu lesen. |
| „Bestätigt, aber nicht heute“ bestätigt nicht | Nur ein exaktes Schlüsselwort als ganze Äußerung führt ohne Modellaufruf aus; alles Längere geht an den Prüfer. | Sag nur das Schlüsselwort, oder antworte klar mit Ja oder Nein. |
| Die Bestätigung bewirkt nach einer Pause nichts | Die ausstehende Aktion ist abgelaufen (`confirmation.ttl`, 60 s). | Frag erneut. |
| `The approval expired; the action was not executed.` | Eine Freigabe im Hintergrund hat `tasks.approvalTtl` überschritten. | Entscheide schneller oder erhöhe die TTL; siehe [Freigaben](tasks/approvals.md). |
| `Background tasks are stopped (kill switch).` | Der Not-Aus ist aktiv. | Drück auf der Seite Aufgaben **Fortsetzen**. |
| `Today's cost cap for background tasks is reached.` | `tasks.dailyCostCap` ist ausgeschöpft. | Warte auf den nächsten lokalen Tag oder erhöhe die Grenze, siehe [`tasks`](../configuration/marshal-yaml/tasks.md). |
| `The task's budget (or its parent's) is used up.` | Die Aufgabe ist `failed` mit `budget_exceeded`. | Leg die Aufgabe mit einem engeren Ziel neu an oder erhöhe `tasks.defaultBudget`. |
| `A schedule may run at most every 5 minutes.` | Cron schneller als `tasks.minWakeupInterval`. | Nimm einen langsameren Zeitplan. |
| `'<x>' is not a five-field cron expression.` | Ungültiger Cron-Ausdruck. | Nimm `minute hour day month weekday`, zum Beispiel `0 6 * * *`. |
| `Low-priority deliveries never use voice; use inbox or push.` und andere Fehler bei Zustellungen | Ungültiger Eintrag in `deliver`. | Siehe [geplante Aufgaben](tasks/scheduled-tasks.md#schritte), Schritt „Wähle die Zustellungen“. |
| `A task named '<x>' already exists.` | Namen sind pro Eigentümer eindeutig. | Nimm `tasks.update` oder einen anderen Namen. |
| Eine Beobachtung löst nie aus | Der Filter passt nicht: falsche `source`, falscher `type`, falsches Glob-Muster `entity` oder Nutzdatenfeld; oder es ist eine Beobachtung mit `once`, die schon gelaufen ist. | Prüfe das Ereignis in [Ereignisse](../reference/events.md); `tasks_list` zeigt `once`. Nicht vertrauenswürdige `ha`-Ereignisse passen trotzdem, sie belasten nur den Durchlauf. |
| Das Briefing wird nicht gesprochen | Die Bedingungen in `when` gelten nie vor `expires`; es wurde stattdessen per Push gesendet. | Prüfe die Sensoren, siehe [tägliches Briefing](tasks/daily-briefing.md). |
| Einem Durchlauf im Kontext eines Satelliten fehlen die Werkzeuge `mail_*` | Der Ursprungssatellit verweigert sie (`denyTools`), und die Richtlinie gilt für alle seine Durchläufe und Kindaufgaben. | Leg die Aufgabe von einem anderen Gerät aus an oder ändere das [Satellitenprofil](../configuration/satellites-yaml.md). |

## Gedächtnis

| Symptom | Ursache | Lösung |
|---|---|---|
| Das Gedächtnis verfehlt einen Fakt in einer anderen Sprache | Das Embedding-Modell ist nicht mehrsprachig. | Nimm ein mehrsprachiges Modell; Marshal bettet beim nächsten Start neu ein. |
| Die Gedächtnisprüfung in `/health` ist unhealthy | Die Erweiterung `sqlite-vec` wurde nicht geladen. | Prüfe die Plattform des Images und den Text in `/health`; nimm das offizielle Image. |
| Falsches Projekt aktiv | Abgeleitete Aktivierung. | Sag „nicht dieses Projekt“ (`memory.correct_project`) oder korrigiere die Alias-Namen in **Gedächtnis**, Projekte. |
| Ein gemerkter Fakt ist weg | Weich gelöscht oder ersetzt. | Öffne den Verlauf des Eintrags in **Gedächtnis**. |

## Backups

| Symptom | Ursache | Lösung |
|---|---|---|
| `No Data Protection keys at /data/keys to back up` | Es wurde nie ein Secret in der Oberfläche gespeichert, also gibt es keine Schlüssel. | Nichts nötig. |
| In der Oberfläche gespeicherte Secrets sind nach einer Wiederherstellung unlesbar | Die Datenbank wurde ohne `keys/` wiederhergestellt. | Stell beide zusammen wieder her, siehe [Backups und Wiederherstellung](backups-and-restore.md). |

## Verwandte Seiten

::: cards
- [Beobachtbarkeit](observability.md)
- [Übersicht der Konfiguration](../configuration/index.md)
- [FAQ](../faq.md)
- [Fehlerbehebung der Home-Assistant-Komponente](../home-assistant-component/troubleshooting.md)
:::
