# Set up a development machine

Install the tools you need to build Marshal, run its tests and work on the web UI and the Home Assistant component.

## Before you start

You need Git and a Linux, macOS or Windows machine that runs Docker.

## Tools

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | `10.0.101` or a later 10.0 feature band | Everything under `src/` and `tests/`. |
| Node.js | 22 | The SPA in `web/`. |
| uv | current | The Python project in `ha/`. |
| Docker | current | Testcontainers tests, building the image. |
| Java or Docker | optional | Rendering PlantUML diagrams. |

### .NET SDK

`global.json` pins the SDK to `10.0.101` with `rollForward: latestFeature`, so any newer 10.0.x feature band works. It also selects Microsoft.Testing.Platform as the test runner. Check the active SDK from the repository root:

```sh
dotnet --version
```

The repository restores local tools from `.config/dotnet-tools.json`, which provides `dotnet-ef`:

```sh
dotnet tool restore
```

### Node.js

`web/package.json` declares no `engines` field and the repository has no `.nvmrc`. The `Dockerfile` builds the SPA with `node:22-alpine`, so use Node 22 locally to match the image. Install the dependencies:

```sh
cd web
npm ci
```

### uv and Python

The component in `ha/` requires Python 3.14.2 or later (`requires-python` in `ha/pyproject.toml`). uv installs a matching interpreter. The `uv.lock` file pins the Home Assistant test dependencies.

```sh
cd ha
uv sync
```

### Docker

Several test projects start containers through Testcontainers (for example an IMAP server and FreshRSS). The user running `dotnet test` needs access to the Docker socket. Without it, those tests fail to start their containers.

Docker also builds the release image. See [release process](release-process.md).

## Build once

From the repository root:

```sh
dotnet build
```

The build treats warnings as errors and enforces the `.editorconfig` style rules (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` in `Directory.Build.props`). Run `dotnet format` before you commit.

## Check that it works

```sh
dotnet test --project tests/Marshal.Core.Tests
```

The run ends with all tests passing. Continue with [building and testing](building-and-testing.md).

## Related

- [Development index](index.md)
- [Web frontend](web-frontend.md)
