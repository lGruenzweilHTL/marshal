---
icon: fa-solid fa-house-signal
order: 40
---

# Connect Home Assistant

Install the Marshal integration, give it an API token and select Marshal as the conversation agent of an Assist pipeline.

## Before you start

- A running Marshal ([quick start](quick-start.md)) that Home Assistant can reach over HTTP or HTTPS.
- Home Assistant 2026.9.0 or newer.

## Steps

::: steps
1. **Install the integration**

   Copy `ha/custom_components/marshal` from the Marshal repository into the `custom_components` folder of your Home Assistant configuration, so that `custom_components/marshal/manifest.json` exists. Restart Home Assistant. For HACS and details see [installing the component](../home-assistant-component/installation.md).

2. **Create an API token**

   In the web interface open **Security** and create a token with the scope `conversation:write`. Or run:

   ```sh
   docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
   ```

   The token is shown once. Copy it.

3. **Add the integration**

   In Home Assistant open **Settings, Devices & services, Add integration, Marshal**. Enter Marshal's base URL (for example `http://marshal.lan:8080`, including a path base if you use one) and the token. The setup checks that Marshal is reachable, supports API version 1 and accepts the token.

4. **Select Marshal in a pipeline**

   1. Open **Settings, Voice assistants** and select your assistant.
   2. Set **Conversation agent** to **Marshal**.
   3. Turn **Prefer handling commands locally** off. Otherwise Home Assistant answers recognised sentences itself and Marshal never sees them.

5. **Let Marshal read your home (optional)**

   Create a long-lived access token of an admin user in Home Assistant, store it as the secret `ha_token` (see [secrets](../guides/secrets.md)), and add to `marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   ha:
     url: http://homeassistant.local:8123
     tokenSecret: ha_token
   ```

   Restart Marshal.
:::

## Check that it works

Open the Assist dialog in Home Assistant and type a question. Marshal's reply appears. A run shows up under **Runs** in the web interface.

## Troubleshooting

See [component troubleshooting](../home-assistant-component/troubleshooting.md).

## Related
- [Home Assistant integration guide](../guides/integrations/home-assistant.md)
- [Satellites](../guides/voice/satellites.md)
