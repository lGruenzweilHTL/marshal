# Marshal documentation

Marshal is a self-hosted AI agent for Home Assistant. This page maps the documentation and suggests a reading path for each audience.

## Reading paths

### New user

1. [Overview](getting-started/overview.md)
2. [Requirements](getting-started/requirements.md)
3. [Quick start](getting-started/quick-start.md)
4. [Connect Home Assistant](getting-started/connect-home-assistant.md)
5. [Next steps](getting-started/next-steps.md)
6. Without Home Assistant yet: [try demo mode](guides/demo-mode.md)

### Voice user

1. [Assist pipeline](guides/voice/assist-pipeline.md)
2. [Satellites](guides/voice/satellites.md)
3. [Follow-ups](guides/voice/follow-ups.md)
4. [Home Assistant component](home-assistant-component/installation.md)
5. [Daily briefing](guides/tasks/daily-briefing.md)

### Integrator

1. [Architecture](concepts/architecture.md)
2. [API overview](api/index.md) and [authentication](api/authentication.md)
3. [Machine tokens](guides/authentication/machine-tokens.md)
4. [Webhooks](api/webhooks.md)
5. [Custom MCP servers](guides/integrations/custom-mcp-servers.md)
6. [Connect MCP clients (Claude Code, VS Code)](guides/integrations/mcp-clients.md)
7. [Tool reference](reference/tools.md)

### Contributor

1. [Contributing](../CONTRIBUTING.md)
2. [Development setup](development/setup.md)
3. [OpenSpec workflow](development/openspec-workflow.md)
4. [Adding an integration](development/adding-an-integration.md) and [adding a tool](development/adding-a-tool.md)
5. [Documentation style](development/docs-style.md)

## Documentation map

| Section | Kind | Use it to |
|---|---|---|
| [Getting started](getting-started/overview.md) | Tutorial | Install Marshal and have a first conversation |
| [Installation](installation/docker-compose.md) | How-to | Deploy, secure, upgrade and remove Marshal |
| [Guides](guides/troubleshooting.md) | How-to | Connect integrations, set up tasks, notifications, displays and more |
| [Home Assistant component](home-assistant-component/installation.md) | How-to | Install and configure the Assist conversation agent and the AI Task entity |
| [Configuration](configuration/index.md) | Reference | Look up every key in `marshal.yaml`, `risk.yaml` and the other `/config` files |
| [Reference](reference/tools.md) | Reference | Look up tools, risk tiers, events, CLI, endpoints and the [glossary](reference/glossary.md) |
| [API](api/index.md) | Reference | Call Marshal from your own code |
| [Concepts](concepts/architecture.md) | Explanation | Understand why Marshal works the way it does |
| [Development](development/index.md) | Contributor | Change Marshal itself |

## More

- [Rate replies and replay eval cases](guides/evals.md)
- [Get suggestions](guides/suggestions.md)
- [Turn repeated workflows into skills](guides/skill-proposals.md)
- [Track maintenance by usage](guides/maintenance.md)
- [Connect web search (SearXNG)](guides/integrations/web-search.md)

- [FAQ](faq.md)
- [Roadmap](roadmap.md)
- [Security model](concepts/security-model.md)
- [Changelog](../CHANGELOG.md)
