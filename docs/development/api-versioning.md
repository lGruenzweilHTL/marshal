# API versioning

Add or change a public API endpoint without breaking a published version.

## Rules

- The API is versioned by URL segment: `/api/v{version:apiVersion}/...` (`Asp.Versioning.Http`).
- Versions are declared once in code, in `src/Marshal.Api/Versioning/ApiVersions.cs`. Never read a version from configuration.
- Endpoint files contain no version literals and no `/api/...` prefix.
- Never change the contract of a published version in place. A breaking change adds a new version; the old one keeps working until it is retired.
- Unversioned routes are only `/health`, `/api/versions`, `/openapi/{document}.json`, auth protocol callbacks and SPA routes.

## Current versions

`ApiVersions` defines `V1 = new(1, 0)` as the only supported version, and an empty deprecated list. `GET /api/versions` returns both lists, unauthenticated. An unknown route or version returns a `404` problem with code `api.unsupported_version` and an `api-supported-versions` header.

## Add an endpoint to the current version

Write the failing API test first (`tests/Marshal.Api.Tests`, `WebApplicationFactory`). Then add a feature file that exposes a `Map<Feature>Endpoints` extension on `RouteGroupBuilder`:

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

Register it on the versioned group in `src/Marshal.Api/Hosting/MarshalApp.cs`:

```csharp
RouteGroupBuilder v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(ApiVersions.V1);
v1.MapTaskEndpoints();
```

Give each endpoint an operation id (`WithName`) and say what it does with `WithDescription`. Do not set a summary: the OpenAPI document names every operation by its route below the version base (`/tasks`), and takes its security requirements from the endpoint's authorization. A policy that `Policies.Credentials` does not map makes OpenAPI generation fail; add the new policy there.

Return typed results (`Results<Ok<T>, NotFound, ...>`). Failed `Result`s become `ProblemDetails`. Do not put `TimeSpan` in a request or response: OpenAPI generation fails with HTTP 500. Use numbers with the unit in the name, such as `wallTimeSeconds`.

Adding an optional field or a new endpoint to V1 is compatible. Removing or renaming a field, changing a type or tightening validation is breaking.

## Add a new version

1. Add `V2` to `ApiVersions`, and include it in `Supported`. `DocumentName` derives the document name `v2`.
2. Create a second group in `MarshalApp.cs` with `.HasApiVersion(ApiVersions.V2)` and map the changed endpoints on it.
3. Keep V1 mapped. When you retire V1, move it to `Deprecated` first.
4. Regenerate the OpenAPI types and update the SPA ([web frontend](web-frontend.md)). Document the new version in the [API reference](../api/index.md).

`MapOpenApi().WithDocumentPerVersion()` serves one document per version.

## Check that it works

- `GET /api/versions` lists the new version.
- Old clients still work against the old version.
- `npm run gen:api` succeeds and `npm run build` passes.

## Related

- [OpenAPI](../api/openapi.md)
- [Web frontend](web-frontend.md)
