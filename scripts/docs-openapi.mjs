// Prepares the API reference of the documentation site (clay.yaml `generate`, before every Clay build).
//
// Reads the committed v1 OpenAPI document (web/openapi/v1.json, kept current by `npm run gen:api` in web/) and writes a
// copy for Clay to artifacts/openapi/marshal-v1.json. The copy adds what the exported document leaves out on purpose,
// so the request cells on the site can call an instance:
//   - servers: a local instance and a placeholder for your own (the export has none; the SPA calls same-origin),
//   - the two ways to authenticate (machine token, browser session), as alternatives on every operation, because the
//     document does not describe which endpoint takes which (or none). docs/api/authentication.md is the reference.
// It never changes paths, operations or schemas: the API contract stays exactly what Marshal serves.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = resolve(root, "web/openapi/v1.json");
const target = resolve(root, "artifacts/openapi/marshal-v1.json");

const document = JSON.parse(readFileSync(source, "utf8"));

document.info = {
  ...document.info,
  title: "Marshal API",
  description:
    "The versioned REST API of a Marshal instance. Machine clients authenticate with a scoped API token " +
    "(`Authorization: Bearer <token>`); most endpoints serve Marshal's own web interface and need a browser session. " +
    "See [API authentication](/api/authentication) for which credential reaches which endpoint.",
};

document.servers = [
  { url: "http://localhost:8080", description: "A local instance, for example one in demo mode" },
  { url: "https://marshal.example.com", description: "Your instance (replace the host)" },
];

document.components ??= {};
document.components.securitySchemes = {
  apiToken: {
    type: "http",
    scheme: "bearer",
    description: "A scoped machine token, created with `marshal tokens create` or under Security in the web interface.",
  },
  session: {
    type: "apiKey",
    in: "cookie",
    name: "marshal_session",
    description: "The browser session of the web interface. State-changing requests also need an `X-XSRF-TOKEN` header.",
  },
};
// Either credential; the few anonymous endpoints (docs/api/index.md) ignore it.
document.security = [{ apiToken: [] }, { session: [] }];

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, JSON.stringify(document, null, 2) + "\n");
