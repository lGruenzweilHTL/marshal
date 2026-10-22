---
icon: fa-solid fa-puzzle-piece
description: "Ein neues externes System hinter einer kleinen Schnittstelle zu Marshal hinzufügen, mit Werkzeugen, Risikostufen, Secrets und Tests."
---

# Eine Integration hinzufügen

Ein neues externes System hinter einer kleinen Schnittstelle zu Marshal hinzufügen, mit Werkzeugen, Risikostufen, Secrets und Tests.

## Bevor du anfängst

- Du hast eine freigegebene OpenSpec-Änderung, die die Integration abdeckt ([OpenSpec-Ablauf](openspec-workflow.md)).
- Prüfe die externe API gegen ihre aktuelle Dokumentation. Verlass dich nicht auf die Designnotizen.
- Entscheide vorab, ob eine Fähigkeit nie existieren darf (zum Beispiel Mail senden). Eine verbotene Fähigkeit bekommt überhaupt keinen Codepfad.

Die Proxmox-Integration (`src/Marshal.Integrations.Proxmox`) ist eine kompakte Vorlage: eine Options-Klasse, eine Client-Schnittstelle mit einer REST-Implementierung, eine Werkzeugquelle und eine Registrierungsmethode.

Ein Coding-Agent kann die Integration aus der freigegebenen Änderung bauen; setz den Namen deiner Änderung für `<change>` ein:

::: prompt Für einen Coding-Agenten
Read CLAUDE.md, STYLE_GUIDE.md and
https://lgruenzweilhtl.github.io/marshal/development/adding-an-integration.md.
Then implement the integration of the OpenSpec change
openspec/changes/<change>/ test first, with the Proxmox integration as the
reference. Verify the external API against its current documentation, keep
every client behind a small interface, and work through the documentation
checklist. Run the tests of the changed
projects and `npx clay-doc@5 check` until both pass.
:::

## Schritte

::: steps
1. **Das Projekt anlegen**

   Leg `src/Marshal.Integrations.<Name>` und `tests/Marshal.Integrations.<Name>.Tests` an. Füg beide zu `Marshal.sln` hinzu. Paketversionen gehören in `Directory.Packages.props`, nie in die Projektdatei. Verweise auf `Marshal.Abstractions`; verweise nur auf die Pakete, die du brauchst.

2. **Eine kleine Schnittstelle festlegen**

   Setz die Fähigkeit hinter eine fokussierte Schnittstelle (`IProxmoxClient`). Drück Unterschiede zwischen Implementierungen mit Fähigkeits-Flags aus, nicht mit `is`-Prüfungen. Erwartete Fehler (Host nicht erreichbar, Entität nicht gefunden) geben ein `Result` zurück, keine Exception.

3. **Optionen mit Validierung ergänzen**

   Binde eine versiegelte Options-Klasse an einen Abschnitt von `marshal.yaml` und validiere sie:

   ```csharp
   services.AddOptions<ProxmoxOptions>().Bind(configuration.GetSection("proxmox"))
       .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true, Scheme: "https" }, "proxmox.url must be an absolute https URL.")
       .ValidateOnStart();
   ```

   Die Konfiguration enthält nie Secrets. Optionen verweisen per Namen auf ein Secret (`tokenSecret: proxmox_token`), und der Client löst den Wert über `ISecretStore` auf. Markiere Eigenschaften der Optionen, die Secrets enthalten, mit `[Secret]`; der Start scheitert dann, wenn ein wörtliches Secret in `/config` steht. Siehe [Secrets-Referenz](../configuration/secrets-reference.md).

4. **Die Dienste registrieren**

   Stell eine Erweiterungsmethode bereit, `services.AddMarshal<Name>(configuration)`, und ruf sie aus `src/Marshal.Api/Hosting/MarshalApp.cs` auf. Registriere HTTP-Clients über `IHttpClientFactory` mit `AddStandardResilienceHandler()`. Wiederhole Aufrufe nur, wenn sie sich gefahrlos wiederholen lassen.

5. **Werkzeuge bereitstellen**

   Implementiere `IToolSource` und registriere die Quelle (`services.AddSingleton<IToolSource, ...>()`). Gib keine Werkzeuge zurück, solange die Integration nicht konfiguriert ist, damit das Modell nie Werkzeuge sieht, die nicht funktionieren können. [Ein Werkzeug hinzufügen](adding-a-tool.md) behandelt Deskriptoren, Stufen und Tests.

   Werkzeug-IDs sind `<namespace>.<snake_case_name>`. Der Namensraum benennt die Fähigkeit, nicht den Anbieter: `printer`, nicht `moonraker`.

6. **Risikostufen zuweisen**

   Jedes Werkzeug hat eine Standardstufe: `safe`, `confirm` oder `forbidden`. Wähle die niedrigste Stufe, die noch sicher ist, und schreib Regeln mit Argumentbezug in `deploy/config.example/risk.yaml`, wenn die Stufe vom Ziel oder von den Argumenten abhängt. Regeln und Sichtbarkeit:

   - Aktionen der Bestätigungsstufe werden auf dem Server eingefroren (Werkzeug, Argumente, Nonce, TTL). Nur eine Benutzeraktion kann bestätigen.
   - Ein Werkzeug, das verboten ist, außerhalb der Allowlist des Agent-Templates liegt, für den Satelliten verboten ist oder auf einem Satelliten ohne Bestätigungsmöglichkeit in der Bestätigungsstufe liegt, wird aus der Werkzeugliste entfernt. Der Guard prüft dieselben Regeln noch einmal.
   - Inhalte der Integration, die von außen kommen (Mail, Feeds, Notizen, Webseiten), sind nicht vertrauenswürdige Daten. Markiere die Werkzeugausgabe als untrusted, damit der Durchlauf belastet wird.
   - Füg nie ein Werkzeug hinzu, das eine vom Aufrufer gewählte URL abruft.

7. **Daten auf den Eigentümer beschränken**

   Jede Zeile, die einem Benutzer gehört, hat `owner_id`, und jede Abfrage läuft über die auf den Eigentümer beschränkten Abfragefilter in `Marshal.Data`. Lies den aktuellen Eigentümer aus dem Ausführungskontext, nie aus einer globalen Variable. Speichert die Integration Zeilen, folge [Datenbankmigrationen](database-migrations.md).

8. **Die Tests zuerst schreiben**

   - Fake das externe System: ein Server im selben Prozess mit `Microsoft.AspNetCore.TestHost`, aufgezeichneter Verkehr oder ein Testcontainers-Image.
   - Nutze die Fakes aus `tests/Marshal.Testing`. Leg neue gemeinsame Fakes dort an.
   - Teste den unkonfigurierten Fall (keine Werkzeuge), die Stufe jedes Werkzeugs und das Eigentum mit einem zweiten Fake-Benutzer.

9. **Die Dateien des Repositorys aktualisieren**

   - `CLAUDE.md`: Eintrag in der Karte des Repositorys, Zeilen im Paketindex für neue Pakete.
   - Das Paketdiagramm unter [Architektur](../concepts/architecture.md#die-teile) und, wenn du Entitäten hinzugefügt hast, das [Datenmodell](../diagrams/erd.md) ([Die Diagramme pflegen](../diagrams/README.md)).
   - `deploy/config.example/`: Beispielkonfiguration für den neuen Abschnitt.
   - `src/Marshal.Demo`: Änderst du eine Schnittstelle, die der Demo-Modus implementiert (Home Assistant, Drucker, Mail, RSS, Notizen), aktualisiere die simulierte Implementierung in derselben Änderung. Eine neue Integration wird nur simuliert, wenn eine OpenSpec-Änderung sie in den Umfang des [Demo-Modus](../guides/demo-mode.md) aufnimmt.
:::

## Checkliste für die Dokumentation

Eine neue Integration ist erst fertig, wenn die Doku passt:

- [ ] Eine Anleitung in `docs/guides/integrations/<name>.md` ([Stil der Dokumentation](docs-style.md)).
- [ ] Eine Konfigurationsseite oder ein Abschnitt unter `docs/configuration/marshal-yaml/`, gelistet unter [`integrations`](../configuration/marshal-yaml/integrations.md).
- [ ] Zeilen für jedes Werkzeug in der [Werkzeug-Referenz](../reference/tools.md), mit Stufe und Sichtbarkeit.
- [ ] Änderungen an Risikostufen in der [Referenz der Risikostufen](../reference/risk-tiers.md), wenn du Regeln hinzufügst.
- [ ] Neue Secrets in der [Secrets-Referenz](../configuration/secrets-reference.md).
- [ ] Neue Ereignisse oder Quellen von Benachrichtigungen in den passenden Referenzseiten.
- [ ] Eine Zeile im Changelog ([Release-Prozess](release-process.md)).

## Prüfen, ob es funktioniert

- Ohne Konfiguration sieht das Modell keines der Werkzeuge der Integration.
- Mit gültiger Konfiguration erscheinen die Werkzeuge und geben Daten aus deinem Fake zurück.
- Der Start scheitert mit einer klaren Meldung bei ungültigen Optionen und bei einem wörtlichen Secret.

## Verwandte Seiten

::: cards
- [Werkzeug-Sichtbarkeit](../concepts/tool-visibility.md)
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
:::
