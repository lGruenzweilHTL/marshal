---
icon: fa-solid fa-file-code
order: 70
---

# OpenAPI and client generation

Marshal publishes one OpenAPI document per API version. Use it to inspect every endpoint and to generate a typed client. This site renders it as the [API v1 reference](v1/index.md).

## Documents

| URL | Authentication | Content |
|---|---|---|
| `/openapi/v1.json` | none | OpenAPI document of API version 1 |

Marshal serves the raw JSON document only; it has no built-in interactive documentation page. Read it in the [API v1 reference](v1/index.md) on this site, open it in any OpenAPI viewer, or import the URL into a tool such as Postman.

```sh
curl https://marshal.example.com/openapi/v1.json -o marshal-v1.json
```

The document covers the web interface endpoints too. The [live events](live-events.md) stream is described only as an envelope.

## The API reference on this site

The [API v1 reference](v1/index.md) has a page per operation, tag and schema, built from the document in the repository (`web/openapi/v1.json`) on every docs build. Its paths, operations and schemas are exactly the ones Marshal serves. For the site, the build adds two things the served document leaves out:

- **Servers:** `http://localhost:8080` and a placeholder for your own host. The served document has none, because the web interface calls the API on its own origin.
- **Authentication:** a machine token (`Authorization: Bearer`) or the browser session cookie, on every operation. The served document does not say which endpoint takes which credential, or none; [API authentication](authentication.md) and the [endpoint list](index.md#base-path) do.

### Try the API from the docs

Every operation page, and pages like [API examples](examples.md), has a request cell: fill in the parameters, select **Authorize** to paste a token, and run it with <kbd>Ctrl</kbd> + <kbd>Enter</kbd>. The token stays in your browser tab and is sent only to the server you picked.

The request goes from your browser straight to your instance. Browsers allow that only when the instance answers with CORS headers for this site, and Marshal sends none. Until it does, a cell reports the request as blocked. Copy the request as **curl**, **JavaScript** or **Python** from the cell and run it where you are, or put a reverse proxy in front of a test instance that adds the headers.

> [!WARNING]
> Never let a docs site reach a production instance with a powerful token. Use a test instance, ideally in [demo mode](../guides/demo-mode.md), and a token with only the scopes you try.

## Export without a running server

`marshal --export-openapi <file>` writes the v1 document to a file without starting the server:

::: code-group
```sh [Container]
docker compose run --rm marshal marshal --export-openapi /tmp/v1.json
```

```sh [Source tree]
dotnet run --project src/Marshal.Api -- --export-openapi openapi/v1.json
```
:::

The exported document has no `servers` entry; set the base URL in your client.

## Generate a client

Any OpenAPI generator works. Marshal's own web interface uses `openapi-typescript` and `openapi-fetch`:

```sh
npx openapi-typescript openapi/v1.json -o src/api/schema.d.ts
```

In the repository, `npm run gen:api` in `web/` exports the document and regenerates the types, so a breaking contract change fails the web build. The same commit updates `web/openapi/v1.json`, and with it the API reference on this site.

Generate against a version's document and pin that version in your client. A published version never changes in place; new behavior arrives in a new version. See [API overview](index.md#versioning).

## Related

- [API v1 reference](v1/index.md)
- [CLI reference](../reference/cli.md)
- [Web frontend development](../development/web-frontend.md)
