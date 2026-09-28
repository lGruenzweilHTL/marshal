# Marshal for Home Assistant

A custom integration that makes [Marshal](../README.md) an Assist conversation agent. Every turn you speak to a
voice satellite or type into the Assist dialog is forwarded to Marshal; Marshal's reply streams back into Home
Assistant, so text-to-speech can start before the whole answer is ready.

The integration only sends the turn itself: the text, its language, the conversation id, the device, satellite and
area it came from, the Home Assistant user, and whether it was spoken or typed. It sends no entity states — Marshal
reads what it needs from Home Assistant on its own.

Requires Home Assistant 2026.9 or newer and a Marshal instance that serves API version 1.

## Installation

### HACS (custom repository)

HACS installs integrations from repositories that have `custom_components/marshal/` at their root. Once the
integration is published as its own repository, add that repository in HACS under
**⋮ → Custom repositories** with the category **Integration**, install **Marshal** and restart Home Assistant.

### Manual

Copy `ha/custom_components/marshal` from this repository into the `custom_components` folder of your Home Assistant
configuration (so that `custom_components/marshal/manifest.json` exists) and restart Home Assistant.

## Create an API token

The integration authenticates with a Marshal API token that has the scope `conversation:write`. Create one either

- in Marshal's web interface under **Security → API tokens**, or
- on the Marshal host, in the directory of its `docker-compose.yml`:

  ```sh
  docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
  ```

The token is shown only once. Copy it for the next step.

## Set up the integration

1. **Settings → Devices & services → Add integration → Marshal.**
2. Enter Marshal's address (for example `http://marshal.local:8080`, including a path base if Marshal runs behind a
   reverse proxy) and the API token.

The setup checks that Marshal is reachable, that it supports this integration's API version and that it accepts
the token, and names the problem if one of them fails.

## Use Marshal in an Assist pipeline

1. **Settings → Voice assistants**, open your assistant (or add one).
2. Set **Conversation agent** to **Marshal**.
3. Turn **Prefer handling commands locally** off. When it is on, Home Assistant answers sentences it recognizes
   itself and Marshal never sees them.
4. Pick speech-to-text and text-to-speech engines as usual. With a streaming text-to-speech engine, replies start
   playing while Marshal is still answering.

Marshal replies in German or English, following the pipeline's language.

When Marshal asks a question (for example to confirm unlocking a door), the satellite keeps listening for your
answer. If Marshal is unreachable or fails, the satellite says so briefly in your language; details are in the Home
Assistant log.

## Development

Tests use [`pytest-homeassistant-custom-component`](https://github.com/MatthewFlamm/pytest-homeassistant-custom-component)
with Marshal mocked; they need [uv](https://docs.astral.sh/uv/):

```sh
cd ha
uv sync
uv run pytest
```
