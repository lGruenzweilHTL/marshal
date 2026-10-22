---
title: Marshal documentation
icon: fa-solid fa-house-signal
description: Marshal is a self-hosted AI agent for Home Assistant. Install it, connect your services, call its API and understand how it keeps you in control.
hero: true
---

Marshal is a self-hosted AI agent for Home Assistant. You talk to it through an Assist voice satellite, the Home Assistant app or its own web interface. It acts on your home, printer, mail, calendars and notes, and it asks first before anything risky happens.

![Marshal Home on desktop: the grouped side navigation, two pending approvals, a printer card and running tasks](images/dashboard.png)

> [!NOTE]
> Marshal has no release yet. You build the image from the repository, and behavior can still change without notice.

## Start here

::: cards
- [Overview](getting-started/overview.md)
- [Quick start](getting-started/quick-start.md)
- [Try demo mode](guides/demo-mode.md)
- [Connect Home Assistant](getting-started/connect-home-assistant.md)
:::

## Reading paths

:::: tabs
== New user
::: steps
1. **Understand what you run:** [overview](getting-started/overview.md) and [requirements](getting-started/requirements.md).
2. **Install it:** [quick start](getting-started/quick-start.md).
3. **Talk to it by voice:** [connect Home Assistant](getting-started/connect-home-assistant.md).
4. **Set up the rest:** [next steps](getting-started/next-steps.md).
5. **No Home Assistant yet?** [Try demo mode](guides/demo-mode.md).
:::
== Voice user
::: steps
1. **Add Marshal to a pipeline:** [Assist pipeline](guides/voice/assist-pipeline.md).
2. **Tune each room:** [satellites](guides/voice/satellites.md).
3. **Answer questions by voice:** [follow-ups](guides/voice/follow-ups.md).
4. **Install the integration:** [Home Assistant component](home-assistant-component/installation.md).
5. **Hear your morning:** [daily briefing](guides/tasks/daily-briefing.md).
:::
== Integrator
::: steps
1. **See the parts:** [architecture](concepts/architecture.md).
2. **Call the API:** [API overview](api/index.md), [authentication](api/authentication.md) and the [API reference](api/openapi.md).
3. **Give clients tokens:** [machine tokens](guides/authentication/machine-tokens.md).
4. **Send events in:** [webhooks](api/webhooks.md).
5. **Extend the tools:** [custom MCP servers](guides/integrations/custom-mcp-servers.md) and [MCP clients](guides/integrations/mcp-clients.md).
6. **Look up a tool:** [tool reference](reference/tools.md).
:::
== Contributor
::: steps
1. **Read the rules:** [contributing](https://github.com/lGruenzweilHTL/marshal/blob/master/CONTRIBUTING.md).
2. **Set up your machine:** [development setup](development/setup.md).
3. **Propose a change:** [OpenSpec workflow](development/openspec-workflow.md).
4. **Add code:** [an integration](development/adding-an-integration.md) or [a tool](development/adding-a-tool.md).
5. **Write docs:** [documentation style](development/docs-style.md).
:::
::::

## The documentation

::: cards
- [Getting started](getting-started/index.md)
- [Installation](installation/index.md)
- [Guides](guides/index.md)
- [Home Assistant component](home-assistant-component/index.md)
- [Configuration](configuration/index.md)
- [Reference](reference/index.md)
- [API](api/index.md)
- [Concepts](concepts/index.md)
- [Development](development/index.md)
:::

## Built to stay in your control

- **Forbidden tools are invisible.** A tool the model may not use is not in its tool list. See [tool visibility](concepts/tool-visibility.md).
- **Risky actions wait for you.** Confirm-tier actions are frozen on the server until you approve them. See [risk and confirmation](concepts/risk-and-confirmation.md).
- **Mail has no send path.** Marshal prepares drafts; you send them. See [mail](guides/integrations/mail.md).
- **Untrusted content stays data.** Mail, RSS, notes and web results never become instructions. See [untrusted content](concepts/untrusted-content.md).

## More

::: cards
- [FAQ](faq.md)
- [Roadmap](roadmap.md)
- [Security model](concepts/security-model.md)
- [Changelog](https://github.com/lGruenzweilHTL/marshal/blob/master/CHANGELOG.md) {fa-solid fa-list}
  Every change, release by release.
:::
