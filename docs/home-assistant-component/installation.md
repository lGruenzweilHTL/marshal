# Install the Home Assistant component

Install the Marshal custom integration so Home Assistant sends Assist conversations to Marshal, then connect it with an API token.

## Before you start

- Home Assistant 2026.9.0 or newer (the version in `hacs.json`).
- A running Marshal instance that Home Assistant can reach over HTTP(S) and that serves API version 1. See [connect Home Assistant](../getting-started/connect-home-assistant.md).
- Access to the Marshal host or web interface to create an API token.

## Steps

### 1. Create an API token

Create a token with only the scope `conversation:write`:

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
```

Or open **Security** in the Marshal web interface and create it there. Copy the token; it is shown once. See [authentication](../api/authentication.md).

### 2. Copy the integration into Home Assistant

The integration lives in `ha/custom_components/marshal` of the Marshal repository. Choose one way.

**Manual.** Copy the `marshal` folder into the `custom_components` folder of your Home Assistant configuration, so that `custom_components/marshal/manifest.json` exists, then restart Home Assistant.

**HACS custom repository.** HACS installs integrations from a repository that has `custom_components/marshal/` at its root. The integration currently sits in a subfolder of the Marshal repository, so HACS can install it only once it is published as its own repository. When it is, open HACS, choose **Custom repositories** from the menu, add the repository URL with the category **Integration**, install **Marshal** and restart Home Assistant. Until then, use the manual method.

### 3. Add the integration

1. Open **Settings > Devices & services > Add integration** and choose **Marshal**.
2. Enter Marshal's address and the token. See [configuration](configuration.md).

### 4. Use Marshal in a voice assistant

1. Open **Settings > Voice assistants** and open or add an assistant.
2. Set **Conversation agent** to **Marshal**.
3. Turn **Prefer handling commands locally** off. When it is on, Home Assistant answers sentences it recognizes and Marshal never sees them.
4. Choose speech-to-text and text-to-speech engines as usual.

For satellites and pipelines, see [Assist pipeline](../guides/voice/assist-pipeline.md) and [satellites](../guides/voice/satellites.md).

## Check that it works

Open the Assist dialog in Home Assistant, pick the assistant that uses Marshal and type a question. Marshal answers. A Marshal device with one conversation entity appears under the integration.

## Related

- [Configuration](configuration.md)
- [Troubleshooting](troubleshooting.md)
