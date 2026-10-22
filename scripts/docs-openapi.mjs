// Prepares the API reference of the documentation site (clay.yaml `generate`, before every Clay build).
//
// Reads the committed v1 OpenAPI document (web/openapi/v1.json, kept current by `npm run gen:api` in web/) and writes a
// copy for Clay to artifacts/openapi/marshal-v1.json. The copy adds the servers the exported document leaves out on
// purpose (the SPA calls the API on its own origin), so the request cells on the site can call an instance, and a
// description for the reference's overview page. Security schemes and requirements come from Marshal itself.
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

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, JSON.stringify(document, null, 2) + "\n");
