# OpenAPI and client generation

Marshal publishes one OpenAPI document per API version. Use it to inspect every endpoint and to generate a typed client.

## Documents

| URL | Authentication | Content |
|---|---|---|
| `/openapi/v1.json` | none | OpenAPI document of API version 1 |

Marshal serves the raw JSON document only; it has no built-in interactive documentation page. Open the document in any OpenAPI viewer, or import the URL into a tool such as Postman.

```sh
curl https://marshal.example.com/openapi/v1.json -o marshal-v1.json
```

The document covers the web interface endpoints too. The [live events](live-events.md) stream is described only as an envelope.

## Export without a running server

`marshal --export-openapi <file>` writes the v1 document to a file without starting the server:

```sh
docker compose run --rm marshal marshal --export-openapi /tmp/v1.json
```

When you work from the source tree:

```sh
dotnet run --project src/Marshal.Api -- --export-openapi openapi/v1.json
```

The exported document has no `servers` entry; set the base URL in your client.

## Generate a client

Any OpenAPI generator works. Marshal's own web interface uses `openapi-typescript` and `openapi-fetch`:

```sh
npx openapi-typescript openapi/v1.json -o src/api/schema.d.ts
```

In the repository, `npm run gen:api` in `web/` exports the document and regenerates the types, so a breaking contract change fails the web build.

Generate against a version's document and pin that version in your client. A published version never changes in place; new behavior arrives in a new version. See [API overview](index.md#versioning).

## Related

- [CLI reference](../reference/cli.md)
- [Web frontend development](../development/web-frontend.md)
