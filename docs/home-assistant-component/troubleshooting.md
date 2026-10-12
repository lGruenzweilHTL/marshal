# Troubleshoot the Home Assistant component

Fixes for the common problems when setting up or using the Marshal integration. For general problems see [troubleshooting](../guides/troubleshooting.md).

## Setup fails

| Symptom | Cause and fix |
|---|---|
| "Marshal is not reachable at this address" | Open the URL in a browser from the Home Assistant host. Check the port, the reverse proxy and the scheme. Home Assistant in a container must resolve the host name. |
| "Marshal does not support this integration's API version" | Marshal is older or newer than the component. `curl <url>/api/versions` must list `1.0`. Update Marshal or the component. |
| "Marshal rejected the API token" | The token is wrong, revoked or expired. Check with `curl -H "Authorization: Bearer <token>" <url>/api/v1/me`. Create a new token with `conversation:write`. |
| "Enter a full http:// or https:// address" | Add the scheme, for example `http://marshal.local:8080`. |
| Path prefix | If Marshal runs under `http.pathBase`, include it in the URL. |

## Marshal does not answer

- **Marshal never sees the sentence.** Turn off **Prefer handling commands locally** in the voice assistant. Home Assistant answers recognized sentences itself otherwise.
- **The assistant uses another agent.** Check that **Conversation agent** is **Marshal**.
- **Spoken error "Marshal is not reachable right now".** Marshal is down, the network path is broken, or the turn exceeded 75 seconds. Check `<url>/health`.
- **Spoken error "Marshal refused access".** The token was revoked or lacks `conversation:write`. Create a new token and re-add the integration.
- **Spoken error "Marshal could not handle that right now".** Marshal returned an error. Check the Home Assistant log for the `Marshal turn failed` warning and Marshal's log or [run history](../guides/observability.md).

## Behavior

- **Answer in the wrong language.** The component sends the pipeline's language. Marshal supports German and English only.
- **Long request answered with "working on it".** The turn hit the latency cap and continues as a background task. Raise `agent.voiceLatencyCap` or `agent.textLatencyCap`; see [agent configuration](../configuration/marshal-yaml/agent.md).
- **The satellite does not keep listening after a question.** Marshal sets this per turn; check that the final answer actually asked something, and that the satellite supports continued conversation.
- **A reply to a spoken question is treated as a new request.** The follow-up expired, came from another satellite, or `extra_system_prompt` did not reach the integration. See [follow-ups](../guides/voice/follow-ups.md).
- **Tool unavailable on one satellite.** The satellite profile may deny it. See [satellites.yaml](../configuration/satellites-yaml.md).
- **Wrong room.** The area comes from the device or satellite entity in Home Assistant. Assign the device to an area.

## Collect logs

Enable debug logging for the integration in Home Assistant's `configuration.yaml`:

```yaml
logger:
  logs:
    custom_components.marshal: debug
```

## Related

- [Installation](installation.md)
- [How it works](how-it-works.md)
