# Configure the Home Assistant component

The integration is set up through a config flow with two fields. It has no options.

## Config flow fields

| Field | Description |
|---|---|
| URL | Address of Marshal, for example `http://marshal.local:8080`. Must be `http://` or `https://`. Include the path prefix if Marshal runs under one (`http.pathBase`). A trailing slash, query and fragment are dropped. |
| API token | A Marshal API token with the scope `conversation:write`. |

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

The integration has no reconfigure step. Remove the entry and add it again. To rotate the token, create a new one, then revoke the old one with `marshal tokens revoke <token-id>` after the new entry works.

## What the integration creates

One Marshal device and one conversation entity named after the device. The entity supports German and English and streaming replies.

## Related

- [Installation](installation.md)
- [How it works](how-it-works.md)
- [Machine tokens](../guides/authentication/machine-tokens.md)
