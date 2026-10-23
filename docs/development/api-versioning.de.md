---
icon: fa-solid fa-code-branch
description: "Einen öffentlichen API-Endpunkt hinzufügen oder ändern, ohne eine veröffentlichte Version zu brechen."
---

# API-Versionierung

Einen öffentlichen API-Endpunkt hinzufügen oder ändern, ohne eine veröffentlichte Version zu brechen.

## Regeln

- Die API wird über ein URL-Segment versioniert: `/api/v{version:apiVersion}/...` (`Asp.Versioning.Http`).
- Versionen werden einmal im Code festgelegt, in `src/Marshal.Api/Versioning/ApiVersions.cs`. Lies eine Version nie aus der Konfiguration.
- Endpunktdateien enthalten keine Versionsliterale und kein Präfix `/api/...`.
- Ändere den Vertrag einer veröffentlichten Version nie nachträglich. Eine inkompatible Änderung fügt eine neue Version hinzu; die alte funktioniert weiter, bis sie ausgemustert wird.
- Unversionierte Routen sind nur `/health`, `/api/versions`, `/openapi/{document}.json`, die Callbacks der Anmeldeprotokolle und die Routen der SPA.

## Aktuelle Versionen

`ApiVersions` legt `V1 = new(1, 0)` als einzige unterstützte Version fest und eine leere Liste veralteter Versionen. `GET /api/versions` gibt beide Listen ohne Authentifizierung zurück. Eine unbekannte Route oder Version gibt ein `404`-Problem mit dem Code `api.unsupported_version` und einem Header `api-supported-versions` zurück.

## Einen Endpunkt zur aktuellen Version hinzufügen

Schreib zuerst den fehlschlagenden API-Test (`tests/Marshal.Api.Tests`, `WebApplicationFactory`). Füg dann eine Feature-Datei hinzu, die eine Erweiterung `Map<Feature>Endpoints` auf `RouteGroupBuilder` bereitstellt:

```csharp
public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder group)
    {
        var tasks = group.MapGroup("/tasks").RequireAuthorization(Policies.TasksRead);
        tasks.MapGet("/", ListTasksAsync)
            .WithName("ListTasks")
            .WithDescription("The user's tasks, newest first.");
        return group;
    }
}
```

Registriere sie auf der versionierten Gruppe in `src/Marshal.Api/Hosting/MarshalApp.cs`:

```csharp
RouteGroupBuilder v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(ApiVersions.V1);
v1.MapTaskEndpoints();
```

Gib jedem Endpunkt eine Operations-ID (`WithName`) und sag mit `WithDescription`, was er tut. Setz keine Summary: Das OpenAPI-Dokument benennt jede Operation nach ihrer Route unter der Basis der Version (`/tasks`) und übernimmt die Sicherheitsanforderungen aus der Autorisierung des Endpunkts. Eine Richtlinie, die `Policies.Credentials` nicht abbildet, lässt die Erzeugung von OpenAPI scheitern; ergänze die neue Richtlinie dort.

Gib typisierte Ergebnisse zurück (`Results<Ok<T>, NotFound, ...>`). Gescheiterte `Result`s werden zu `ProblemDetails`. Setz kein `TimeSpan` in eine Anfrage oder Antwort: Die Erzeugung von OpenAPI scheitert dann mit HTTP 500. Nutze Zahlen mit der Einheit im Namen, etwa `wallTimeSeconds`.

Ein optionales Feld oder ein neuer Endpunkt in V1 ist kompatibel. Ein Feld zu entfernen oder umzubenennen, einen Typ zu ändern oder die Validierung zu verschärfen ist inkompatibel.

## Eine neue Version hinzufügen

1. Füg `V2` zu `ApiVersions` hinzu und nimm sie in `Supported` auf. `DocumentName` leitet den Dokumentnamen `v2` ab.
2. Leg in `MarshalApp.cs` eine zweite Gruppe mit `.HasApiVersion(ApiVersions.V2)` an und bilde die geänderten Endpunkte darauf ab.
3. Lass V1 abgebildet. Wenn du V1 ausmusterst, verschieb sie zuerst nach `Deprecated`.
4. Erzeuge die OpenAPI-Typen neu und aktualisiere die SPA ([Weboberfläche](web-frontend.md)). Dokumentiere die neue Version in der [API-Referenz](../api/index.md).
5. Bevor du die Doku änderst, friere die alte Version ein (`npx clay-doc@5 version <old version>`) und aktualisiere dann `docs/latest/`, wie `CLAUDE.md` („Docs site and versions“) beschreibt.

`MapOpenApi().WithDocumentPerVersion()` liefert ein Dokument pro Version aus.

## Prüfen, ob es funktioniert

- `GET /api/versions` listet die neue Version.
- Alte Clients funktionieren weiter gegen die alte Version.
- `npm run gen:api` gelingt, und `npm run build` besteht.

## Verwandte Seiten

::: cards
- [OpenAPI](../api/openapi.md)
- [Weboberfläche](web-frontend.md)
:::
