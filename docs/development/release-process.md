---
icon: fa-solid fa-tag
description: "How a Marshal release is built and published; there is no release automation yet."
---

# Release process

How a Marshal release is built and published. There is no release automation yet: the only workflow, `.github/workflows/docs.yaml`, deploys the docs site. No CI runs tests or publishes images, and no version has been tagged.

## Versions

- App version: `<Version>` in `Directory.Build.props` (shown in the system view), or `-p:Version=<x.y.z>` at build time.
- Home Assistant component: `version` in `ha/custom_components/marshal/manifest.json`, distributed through HACS (`ha/hacs.json`).
- API versions are separate and live in code ([API versioning](api-versioning.md)). Docs versions follow the API version, not the app version (see `CLAUDE.md`).

## The image

The root `Dockerfile` builds one image, the API host plus the SPA:

| Stage | Does |
|---|---|
| `build` | Publishes `src/Marshal.Api` in Release, exports the OpenAPI document (`marshal --export-openapi`), copies `web/src/artifacts/highlighting/languages.json` for the code highlighter. |
| `web` | `npm ci` and `npm run build` against the exported document, so an API change the SPA does not handle fails the image build. |
| `runtime` | `aspnet:10.0` with `git` and `openssh-client`, runs as the unprivileged `app` user on port 8080, links `marshal` into `/usr/local/bin`. |

`Marshal.ExecutorHost` has its own image and is never part of this one.

## Release checklist

::: steps
1. **Verify.** Every change is archived. All .NET test projects, `npm test`, `npm run build` and `uv run pytest` pass. Diagrams and docs match the code.
2. **Version.** Set the app version, and the component version if `ha/` changed.
3. **Changelog.** Move the `[Unreleased]` entries of `CHANGELOG.md` (Keep a Changelog) under the new version. Call out migrations, new API versions and config changes; the [upgrading guide](../installation/upgrading.md) must mention them.
4. **Build and tag.** `docker build -t marshal:<version> .`, `git tag v<version>`, push both. HACS picks up the component from the tag.
:::

## Check that it works

- The new image starts and `GET /health` answers.
- The system view shows the new version.
- An existing `/data` volume migrates on first start.

## Related

::: cards
- [Upgrading](../installation/upgrading.md)
- [API versioning](api-versioning.md)
:::
