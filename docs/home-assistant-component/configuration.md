---
icon: fa-solid fa-sliders
order: 20
description: "The integration is set up through a config flow with two fields and has a reauthentication step for a new token."
---

# Configure the Home Assistant component

The integration is set up through a config flow with two fields and has a reauthentication step for a new token. It has no options.

## Config flow fields

| Field | Description |
|---|---|
| URL | Address of Marshal, for example `http://marshal.local:8080`. Must be `http://` or `https://`. Include the path prefix if Marshal runs under one (`http.pathBase`). A trailing slash, query and fragment are dropped. |
| API token | A Marshal API token with the scopes `conversation:write` (Assist) and `aitasks:write` (AI Task entity). Without `aitasks:write`, conversations work and AI tasks fail. |

Before it creates the entry, the flow checks, in order:

1. `GET /api/versions` lists API version `1.0`.
2. `GET /api/v1/me` accepts the token.

| Error shown | Cause |
|---|---|
| Enter a full http:// or https:// address | The URL is not an `http(s)` URL with a host. |
| Marshal is not reachable at this address | Connection failed or timed out (5 s connect, 10 s total for the checks). |
| Marshal does not support this integration's API version | `/api/versions` does not list `1.0`. |
| Marshal rejected the API token | Marshal answered `401` or `403`. |
| Unexpected answer from Marshal; see the log | Any other answer, for example a non-JSON response. |

The entry title is `Marshal (<host>)`. The URL is the entry's unique id: adding the same instance twice aborts with "This Marshal instance is already configured".

## Options

None. Timeouts, tools, language and satellite behavior are configured in Marshal: [`agent`](../configuration/marshal-yaml/agent.md) and [`satellites.yaml`](../configuration/satellites-yaml.md).

## Change the URL or token

To change the URL, remove the entry and add it again.

To change the token, use the reauthentication step. HA starts it when an AI task gets `401` or `403` from Marshal and shows it under **Settings > Devices & services**. The step asks for the token only, checks it against `GET /api/v1/me` and requires the scope `aitasks:write`:

| Error shown | Cause |
|---|---|
| Marshal rejected the API token | Marshal answered `401` or `403`. |
| The API token lacks the scope aitasks:write | The token is valid but does not carry `aitasks:write`. |

On success the entry keeps its URL and reloads with the new token. Revoke the old token with `marshal tokens revoke <token-id>` afterwards.

## What the integration creates

One Marshal device with two entities named after it:

- a conversation entity (`conversation.marshal`) for Assist, with German and English and streaming replies;
- an AI Task entity (`ai_task.marshal`) that generates data, with attachments, for automations. See [AI tasks](ai-tasks.md). It does not generate images.

## Related

::: cards
- [Installation](installation.md)
- [How it works](how-it-works.md)
- [Run AI tasks from automations](ai-tasks.md)
- [Machine tokens](../guides/authentication/machine-tokens.md)
:::
