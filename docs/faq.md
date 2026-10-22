---
icon: fa-solid fa-circle-question
order: 100
description: "Short answers to common questions about what Marshal is, what it can do and what it deliberately cannot do."
---

# Frequently asked questions

Short answers to common questions about what Marshal is, what it can do and what it deliberately cannot do.

## General

::: details Is there a release or a published image?
No. Marshal has no release yet. You build the image from the repository. See the [quick start](getting-started/quick-start.md).
:::

::: details Can I try Marshal without connecting my services?
Yes. Turn on [demo mode](guides/demo-mode.md). Every integration you have not configured is then simulated, and the views show sample history. You still need a chat model.
:::

::: details Does Marshal need the cloud?
No. Marshal runs on your own hardware and works with fully local models through Ollama or an OpenAI-compatible local server. Only the services you connect, such as a cloud model or a Gmail account, are reached over the internet. See [models setup](installation/models-setup.md).
:::

::: details Which languages does it support?
The web app and the agent work in English and German. See [language](guides/web-ui/language.md).
:::

::: details Can several people share one instance?
Not yet. One user per instance is supported. The data model is prepared for more: every user-owned row has an owner. See [data ownership](concepts/data-ownership.md).
:::

## Safety

::: details Why can Marshal not send mail?
By design. Marshal reads mail and prepares drafts in your drafts folder, but it has no send path: no SMTP configuration and no send API. You review and send the draft yourself. See [mail](guides/integrations/mail.md).
:::

::: details How does Marshal decide when to ask me first?
Every tool has a risk tier. Harmless tools run, risky tools need your confirmation, and forbidden tools are not shown to the model at all. See [risk and confirmation](concepts/risk-and-confirmation.md) and [risk rules](guides/risk-rules.md).
:::

::: details Can a prompt injection in an email or web page make Marshal act?
Content from mail, RSS, notes and web pages is untrusted data. A run that read it is tainted, and its memory and task writes need confirmation. No tool fetches a URL chosen by the model. See [untrusted content](concepts/untrusted-content.md).
:::

::: details Where are my secrets?
Not in `/config`. The configuration holds only secret names, and values come from environment variables, files under `/run/secrets` or the encrypted database. Startup fails when it finds a literal secret. See [secrets](guides/secrets.md).
:::

## Operation

::: details Marshal does not start. What now?
Run `docker compose logs marshal`. Startup lists every configuration problem with its file and key. See [troubleshooting](guides/troubleshooting.md).
:::

::: details Where is my data, and how do I back it up?
Everything Marshal stores is in `/data`: the SQLite database, encryption keys, backups and the notes repository. See [backups and restore](guides/backups-and-restore.md) and [volumes and layout](installation/volumes-and-layout.md).
:::

::: details Does Marshal change my servers, code or notes?
No. The Proxmox and GitHub integrations are read-only. For notes and code, Marshal prepares proposals that you review and approve. See [Proxmox](guides/integrations/proxmox.md), [GitHub](guides/integrations/github.md) and [notes](guides/integrations/notes.md).
:::

::: details Can Marshal run code?
Only through the optional executor host, a separate service that runs coding sessions in sandbox containers. Marshal never holds a Docker socket. See [executor host](installation/executor-host.md).
:::

## Integration

::: details Which Home Assistant versions work?
See [supported versions](reference/supported-versions.md).
:::

::: details Can I use Marshal without Home Assistant voice?
Yes. The web app and the [API](api/index.md) work on their own, although most integrations assume Home Assistant is present.
:::

::: details Can I add my own tools?
Yes, through MCP servers. See [custom MCP servers](guides/integrations/custom-mcp-servers.md). Tools from unmatched MCP servers default to the confirm tier.
:::
