---
icon: fa-solid fa-user-lock
description: "Wie Marshal die Daten jedes Benutzers getrennt hält, obwohl eine Instanz heute einen Benutzer hat."
---

# Datenbesitz

Diese Seite erklärt, wie Marshal die Daten jedes Benutzers getrennt hält, obwohl eine Instanz heute einen Benutzer hat.

## Das Problem

Marshal läuft vorerst mit einem Benutzer pro Instanz. Es ist verlockend, „den Benutzer“ aus einer globalen Variable zu lesen und Prüfungen des Eigentümers wegzulassen. Das geht gut, bis eine zweite Person im Haushalt ein Konto bekommt, und dann ist jede Abfrage, jedes Werkzeug und jeder Hintergrundjob ein Leck. Eigentümerschaft später nachzurüsten heißt, jede Tabelle und jede Zeile des Datenzugriffs anzufassen. Eine einzige vergessene `WHERE`-Klausel zeigt deine Mail-Zusammenfassungen jemand anderem.

## Die Regel

**Jede Zeile, die einem Benutzer gehört, hat `owner_id`, und jeder Datenzugriff läuft über Abfragen, die auf den Eigentümer beschränkt sind. „Den Benutzer“ nie aus einer globalen Variable lesen.**

## Wie es funktioniert

### Ein Eigentümer in jeder Zeile

Jede Tabelle mit Benutzerdaten, auch Kindtabellen, hat ein nicht leeres `owner_id` vom Typ `OwnerId`. Ein Schritt eines Durchlaufs trägt den Eigentümer seines Durchlaufs; eine Version eines Gedächtniseintrags trägt den Eigentümer ihres Eintrags. Die Ausnahmen:

- Tabellen von ASP.NET Identity (sie enthalten die Benutzer selbst)
- `UserPreferences`, mit der ID des Benutzers als Schlüssel
- `Secret`, bei dem ein leerer Eigentümer ein Secret der Instanz bedeutet
- Pins von MCP-Werkzeugen, die Konfiguration der Instanz sind und keine Eigentümerspalte haben; der MCP-Client liest sie unabhängig vom Ausführungskontext

Ein leerer Eigentümer bedeutet nur bei `Secret` „gilt für die Instanz“. Gemeinsame Daten des Haushalts nutzen, wenn sie kommen, eine explizite Spalte für den Geltungsbereich statt eines fehlenden Eigentümers.

### Der Ausführungskontext

Jeder Durchlauf trägt einen Ausführungskontext: `{ OwnerId, Source, InputMode, Satellite, Language, Taint }`. Werkzeuge, der Guard und der Datenzugriff lesen den Benutzer daraus. Ein Werkzeug, das für Eigentümer A läuft, kann nur Daten von Eigentümer A lesen und schreiben.

Der Eigentümer einer Sitzung wird einmal bestimmt, beim ersten Turn, von `IIdentityResolver` aus den Hinweisen, die der Client schickt (`haUserId`, `deviceId`, `satelliteId`). Er wird mit den Hinweisen gespeichert und für diese Sitzung nie neu bestimmt. In dieser Version liefert der Resolver immer den Eigentümer der Instanz, mit `resolved_by = owner_default`. Die Hinweise werden schon aufgezeichnet, ein späterer Resolver kann also Stimmen oder Geräte verschiedenen Personen zuordnen, ohne das Datenmodell zu ändern.

Arbeit im Hintergrund folgt derselben Regel. Eine Aufgabe läuft ohne Anfrage als ihr Eigentümer. Die Ereignisse eines Webhooks gehören dem Eigentümer des Tokens, das sie geschickt hat. Ein per CLI erstelltes Token gehört dem Eigentümer der Instanz.

### Auf den Eigentümer beschränkte Abfragen

Jeder Datenzugriff läuft über globale Abfragefilter von EF Core auf den Eigentümer des aktuellen Ausführungskontexts. Code, der als A läuft und Sitzungen auflistet, bekommt nur die Sitzungen von A; das Artefakt eines anderen Eigentümers verhält sich wie nicht gefunden. Zwei weitere Schutzmaßnahmen sichern das ab:

- Eine Abfrage auf Daten mit Eigentümer **ohne** Ausführungskontext scheitert, statt ungefilterte Zeilen zu liefern.
- Eine Zeile zu schreiben, die einem anderen Eigentümer gehört, wird abgelehnt.

Abfragen auf `Secret` liefern auch die Zeilen der Instanz. Abfragen aus `/config` lesen nie die Secret-Zeilen eines Benutzers. Siehe [Secrets](../guides/secrets.md).

### Sitzungen lassen sich nicht über die ID kapern

Sitzungen von Token-Clients werden über das Token und die Unterhaltungs-ID des Clients zusammen gesucht, nie über die Unterhaltungs-ID allein. Zwei Tokens, die dieselbe `conversationId` schicken, bekommen zwei getrennte Sitzungen. Ein Web-Benutzer, der die Sitzungs-ID eines anderen Benutzers schickt, bekommt 404, und kein Durchlauf startet.

### Typisierte IDs

Domänenentitäten nutzen stark typisierte IDs (`OwnerId`, `SessionId`, `RunId`, `PendingActionId`, `ApiTokenId` und andere). Eine `RunId` dort zu übergeben, wo eine `SessionId` erwartet wird, kompiliert nicht, eine ganze Klasse von Verwechslungen kann also die Produktion nicht erreichen.

### Identitäten pro Eigentümer in abgeleiteten Daten

Die IDs von Gedächtniseinträgen sind ein Hash, der den Eigentümer enthält, zwei Eigentümer mit demselben Titel bekommen also verschiedene Einträge. Artefakte liegen unter `/data/artifacts/<owner>/` mit einem Kontingent pro Eigentümer. Exporte gibt es pro Eigentümer.

### Erwartbare Fehler als Ergebnisse

Erwartbare Fehler werden als `Result` / `Result<T>` mit stabilem Code und einer Meldung zurückgegeben, und API-Endpunkte bilden sie auf `ProblemDetails` ab; `session.not_found` wird zu einem 404 mit diesem Code. Eine unbehandelte Ausnahme wird zu einem 500 mit dem Code `server.unexpected`, einer allgemeinen Meldung und der Trace-ID der Anfrage. Der Body enthält nie Meldung, Typ oder Stacktrace der Ausnahme, interne Pfade und Daten gelangen also nicht über Fehler nach außen.

### Migrationen

EF-Core-Migrationen laufen beim Start automatisch. Vor jeder ausstehenden Migration kopiert Marshal die SQLite-Datei mit Zeitstempel daneben, ein gescheitertes Upgrade lässt sich also zurückrollen. Siehe [Aktualisieren](../installation/upgrading.md) und [Datenbankmigrationen](../development/database-migrations.md).

## Abwägungen

- **Kosten jetzt für später.** Jede Tabelle trägt eine Eigentümerspalte und jede Abfrage einen Filter, obwohl heute ein Benutzer die Regel ist. Die Kosten sind klein; das Nachrüsten wäre es nicht.
- **Lautes Scheitern ohne Kontext.** Codepfade, die vergessen, einen Ausführungskontext zu setzen, stürzen ab, statt still zu funktionieren. Das ist gewollt: Ein Absturz ist sichtbar, ein Leck nicht.
- **Ein Resolver für alle in v1.** Jeder, der mit einem Satelliten spricht, ist der Eigentümer der Instanz. Teile eine Instanz nicht zwischen Personen, die die Daten der anderen nicht sehen dürfen, solange es keine Zuordnung pro Person gibt.

## Verwandte Seiten

::: cards
- [Datenschutz](privacy.md)
- [Sicherheitsmodell](security-model.md)
- [Gedächtnismodell](memory-model.md)
- [Datenverzeichnis](../reference/data-directory.md)
:::
