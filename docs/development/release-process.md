---
icon: fa-solid fa-tag
description: "Build the Marshal image and publish a version."
---

# Release process

Build the Marshal image and publish a version. Marshal has no release automation yet; this page describes the manual process honestly.

## What exists

- A multi-stage `Dockerfile` at the repository root builds a single image: the API host plus the SPA.
- `Directory.Build.props` sets the app version (`<Version>0.1.0</Version>`), shown in the system view. Its comment says CI may override it with `-p:Version=...`.
- `deploy/docker-compose.yml` runs the image.

## What does not exist

- There is no `.github/workflows` directory, so no CI, no automated test run and no image publishing.
- There is no changelog generator and no tagged release yet. `CHANGELOG.md` follows Keep a Changelog and is edited by hand; commits follow Conventional Commits, so you can derive entries from `git log`.

## Image build

The Dockerfile runs three stages:

1. `build` publishes `src/Marshal.Api` in Release and exports the OpenAPI document with `marshal --export-openapi`. It copies `web/src/artifacts/highlighting/languages.json` because the code highlighter embeds it.
2. `web` (`node:22-alpine`) runs `npm ci` and `npm run build` against that document. A contract change the SPA does not handle fails here.
3. `runtime` (`mcr.microsoft.com/dotnet/aspnet:10.0`) adds `git` and `openssh-client`, copies the published host and the SPA into `wwwroot`, and runs as the unprivileged `app` user on port 8080.

The `marshal` command is linked into `/usr/local/bin`, so `docker exec <container> marshal tokens ...` works.

The coding executor is a separate service with its own image. It is never part of the main image.

## Steps

::: steps
1. **Prepare**

   - Make sure every change is archived in OpenSpec ([workflow](openspec-workflow.md)).
   - Run the suites from [building and testing](building-and-testing.md): every .NET test project, `npm test`, `npm run build`, `uv run pytest`.
   - Confirm the diagrams match the code ([diagrams README](../diagrams/README.md)) and the documentation matches the specs.

2. **Version**

   Set `<Version>` in `Directory.Build.props`, or pass `-p:Version=<x.y.z>` when you build. Add the Home Assistant component version in `ha/custom_components/marshal/manifest.json` if the component changed.

3. **Changelog**

   Summarize the Conventional Commit history since the last tag, grouped by `feat`, `fix` and breaking changes. Move the `[Unreleased]` entries in `CHANGELOG.md` under the new version heading and reuse them as release notes. Call out new migrations, new API versions and config changes, which the [upgrading guide](../installation/upgrading.md) must mention.

4. **Build and tag**

   ```sh
   docker build -t marshal:<version> .
   git tag v<version>
   ```

   Push the image to the registry you use and push the tag. The repository does not define a registry.

5. **Publish the Home Assistant component**

   The component in `ha/` is distributed through HACS (`ha/hacs.json`). Tag the repository so HACS can pick up the release.
:::

## Check that it works

- `docker run` of the new image starts and `GET /health` answers.
- The system view shows the new version.
- An existing `/data` volume migrates on first start ([upgrading](../installation/upgrading.md)).

## Related

::: cards
- [Setup](setup.md)
- [Upgrading](../installation/upgrading.md)
:::
