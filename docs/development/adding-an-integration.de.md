---
icon: fa-solid fa-puzzle-piece
description: "Regeln, um Marshal ein externes System hinzuzufügen: Projektaufbau, Schnittstelle, Optionen, Secrets, Werkzeuge, Tests und die Dateien, die mitwandern."
---

# Eine Integration hinzufügen

Regeln, um Marshal ein externes System hinzuzufügen. `src/Marshal.Integrations.Proxmox` ist die kompakte Referenz: eine Optionsklasse, eine Client-Schnittstelle mit einer REST-Implementierung, eine Werkzeugquelle und eine Registrierungsmethode.

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

## Projekt

- `src/Marshal.Integrations.<Name>` und `tests/Marshal.Integrations.<Name>.Tests`, beide in `Marshal.sln`.
- Referenzier `Marshal.Abstractions` (und `Marshal.Data` nur, wenn die Integration Zeilen speichert). Paketversionen gehören in `Directory.Packages.props`.
- Prüf die externe API gegen ihre aktuelle Dokumentation, nicht gegen die Designnotizen.

## Schnittstelle und Optionen

- Eine fokussierte Client-Schnittstelle (`IProxmoxClient`). Drück Unterschiede zwischen Implementierungen mit Capability-Flags aus, nicht mit `is`-Prüfungen. Erwartete Fehler (Host nicht erreichbar, Entität nicht gefunden) geben ein `Result` zurück.
- Eine versiegelte Optionsklasse, gebunden an einen Abschnitt von `marshal.yaml` und mit `ValidateOnStart()` geprüft:

  ```csharp
  services.AddOptions<ProxmoxOptions>().Bind(configuration.GetSection("proxmox"))
      .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true, Scheme: "https" }, "proxmox.url must be an absolute https URL.")
      .ValidateOnStart();
  ```

- Optionen enthalten **Namen** von Secrets (`tokenSecret: proxmox_token`); der Client löst die Werte über `ISecretStore` auf. Markier diese Eigenschaften mit `[Secret]`, damit der Start an einem wörtlichen Secret in `/config` scheitert.

## Registrierung

- Eine Erweiterungsmethode, `services.AddMarshal<Name>(configuration)`, aufgerufen in `src/Marshal.Api/Hosting/MarshalApp.cs`.
- HTTP-Clients über `IHttpClientFactory` mit `AddStandardResilienceHandler()`. Wiederhol nur Aufrufe, die sich gefahrlos wiederholen lassen.
- Eine `IToolSource`, die **keine Werkzeuge liefert, solange die Integration nicht konfiguriert ist**. Werkzeuge folgen [Ein Werkzeug hinzufügen](adding-a-tool.md).

## Sicherheitsregeln

- Jedes Werkzeug hat eine Standardstufe; argumentabhängige Regeln gehören in `deploy/config.example/risk.yaml`.
- Werkzeuge, die verboten sind, außerhalb der Allowlist des Templates liegen, für den Satelliten gesperrt sind oder auf einem Satelliten ohne Bestätigung zur Bestätigungsstufe gehören, fliegen aus der Liste. Der Guard prüft erneut.
- Inhalte von außen (Mail, Feeds, Notizen, Webseiten) sind nicht vertrauenswürdig: Markier die Werkzeugausgabe, damit der Durchlauf belastet wird.
- Zeilen mit Eigentümer tragen `owner_id` und laufen über die Filter pro Eigentümer ([Datenbankmigrationen](database-migrations.md)). Lies den Eigentümer aus dem Ausführungskontext.

## Tests

- Fake das externe System: ein `TestHost`-Server im Prozess, aufgezeichneter Verkehr oder ein Testcontainers-Image.
- Deck den unkonfigurierten Fall (keine Werkzeuge), die Stufe jedes Werkzeugs und das Eigentum mit einem zweiten Fake-Benutzer ab.

## In derselben Änderung aktualisieren

- [ ] `CLAUDE.md`: Karte des Repositorys, Zeilen im Paketindex.
- [ ] Das [Paketdiagramm](../concepts/architecture.md#die-teile) und, bei neuen Entitäten, das [Datenmodell](../diagrams/erd.md).
- [ ] `deploy/config.example/`: der neue Abschnitt.
- [ ] `src/Marshal.Demo`, wenn du eine Schnittstelle geändert hast, die es simuliert. Eine neue Integration wird nur simuliert, wenn eine Änderung sie in den Umfang des [Demo-Modus](../guides/demo-mode.md) aufnimmt.
- [ ] Eine Anleitung in `docs/guides/integrations/<name>.md` und ein Abschnitt unter `docs/configuration/marshal-yaml/`, gelistet in [`integrations`](../configuration/marshal-yaml/integrations.md).
- [ ] Zeilen in der [Werkzeugreferenz](../reference/tools.md); geänderte Regeln in der [Referenz der Risikostufen](../reference/risk-tiers.md); neue Secrets in der [Secrets-Referenz](../configuration/secrets-reference.md); neue Ereignisse oder Benachrichtigungsquellen in ihren Referenzseiten.
- [ ] Eine Zeile in `CHANGELOG.md`.

## Prüfen, ob es funktioniert

- Nicht konfiguriert: Das Modell sieht keines der Werkzeuge der Integration.
- Konfiguriert: Die Werkzeuge erscheinen und liefern Daten von deinem Fake.
- Der Start scheitert mit klarer Meldung an ungültigen Optionen und an einem wörtlichen Secret.

## Verwandte Seiten

::: cards
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
:::
