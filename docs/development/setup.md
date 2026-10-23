---
icon: fa-solid fa-laptop-code
description: "Tool versions Marshal pins and the commands that prepare a checkout."
---

# Set up a development machine

Tool versions Marshal pins and the commands that prepare a checkout.

| Tool | Version | Pinned by |
|---|---|---|
| .NET SDK | `10.0.101` or a later 10.0 feature band | `global.json` (`rollForward: latestFeature`); also selects Microsoft.Testing.Platform |
| Node.js | 22 | The `node:22-alpine` stage of the `Dockerfile`; `web/` has no `engines` field |
| uv, Python | Python 3.14.2 or later | `ha/pyproject.toml`, `ha/.python-version`; uv installs the interpreter |
| Docker | current | Testcontainers (IMAP, FreshRSS) and the image build |

## Prepare a checkout

::: code-group
```sh [.NET]
dotnet tool restore   # dotnet-ef from .config/dotnet-tools.json
dotnet build
```

```sh [web/]
cd web
npm ci
```

```sh [ha/]
cd ha
uv sync
```
:::

The build treats warnings as errors and enforces the `.editorconfig` style (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` in `Directory.Build.props`). Run `dotnet format` before you commit.

The user that runs `dotnet test` needs access to the Docker socket, or the Testcontainers tests fail.

## Related

::: cards
- [Building and testing](building-and-testing.md)
- [Web frontend](web-frontend.md)
:::
