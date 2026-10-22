# Receive Uptime Kuma and Proxmox webhooks

Send Uptime Kuma and Proxmox VE notifications to Marshal so that watches can react when a monitor goes down, a certificate is about to expire or a backup fails.

## Before you start

- Marshal reachable from Uptime Kuma or Proxmox at an HTTPS address, for example `https://<marshal>`. See [reverse proxy](../../installation/reverse-proxy.md).
- Shell access to the Marshal container to create a token.
- Uptime Kuma, or Proxmox VE 8.3 or later with the notification system.

## How webhook ingest works

Marshal accepts webhooks at `POST /api/v1/hooks/{source}`. Each source has its own token scope `hooks:<source>`, and the event's owner is the token's owner. Marshal turns a webhook into an event on the event bus and does not call a model. A watch task then fires on the event. See [event triggers](../tasks/event-triggers.md).

| Response | Meaning |
|---|---|
| 202 | Accepted. Also returned when no event was published (a repeat within the debounce window, a test notification, an ignored type). |
| 400 | The body could not be read. Nothing was published. |
| 401 | Token missing, revoked or expired. |
| 403 | The token lacks the scope `hooks:<source>`. |
| 404 | No such source. |
| 413 | Body larger than 64 KB. |
| 429 | The token sent more than `webhooks.requestsPerMinute` requests in a minute. |

Marshal drops an event whose source, type and entity equal an event it published for the same owner within `webhooks.debounceSeconds`. A change of state, such as `down` followed by `up`, is not a repeat.

```yaml
webhooks:
  debounceSeconds: 60      # default 60
  requestsPerMinute: 60    # per token, default 60
```

The events of these sources are trusted. Their payloads hold only ids, names and numbers, never free text from the sender.

## Uptime Kuma

::: steps
1. **Create a token**

   ```sh
   docker exec marshal marshal tokens create --name kuma --scope hooks:uptime-kuma
   ```

   Marshal prints the token once. Copy it. See [machine tokens](../authentication/machine-tokens.md).

2. **Add a webhook notification in Uptime Kuma**

   Open **Settings > Notifications > Setup Notification** and choose **Webhook**:

   | Field | Value |
   |---|---|
   | Post URL | `https://<marshal>/api/v1/hooks/uptime-kuma` |
   | Request Body | `application/json` (form data also works) |
   | Additional Headers | `{"Authorization": "Bearer <token>"}` |

   Enable the notification on the monitors you want, or for all monitors.

3. **Events**

   | Event type | When | Entity | Payload |
   |---|---|---|---|
   | `monitor.down` | heartbeat status 0 | monitor name | monitor id, name, type, URL, heartbeat time, ping in ms, `important` |
   | `monitor.up` | heartbeat status 1 | monitor name | same |
   | `monitor.pending` | heartbeat status 2 | monitor name | same |
   | `monitor.maintenance` | heartbeat status 3 | monitor name | same |
   | `certificate.expiring` | certificate notice without heartbeat | monitor name | monitor name, days left |

   Uptime Kuma's test message and unknown notices publish nothing. The payload never contains Uptime Kuma's `msg` text.

4. **Create a watch**

   Ask Marshal: "Tell me when the NAS goes down." Marshal creates a watch with `source: uptime-kuma`, `type: monitor.down`, `entity: NAS`.
:::

## Proxmox VE webhooks

::: steps
1. **Create a token**

   ```sh
   docker exec marshal marshal tokens create --name proxmox --scope hooks:proxmox
   ```

2. **Add a webhook target in Proxmox**

   Open **Datacenter > Notifications > Add > Webhook** and set:

   | Field | Value |
   |---|---|
   | Method and URL | `POST https://<marshal>/api/v1/hooks/proxmox` |
   | Header | `Authorization` = `Bearer {{ secrets.token }}` |
   | Header | `Content-Type` = `application/json` |
   | Body | `{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}` |
   | Secret | `token` = the token from step 1 |

   Then add a notification matcher that sends to this target. Without a matcher, Proxmox sends nothing.

3. **Events**

   | Event type | Proxmox notification | Entity |
   |---|---|---|
   | `backup.failed` | `vzdump` with severity `error` | job id, else hostname |
   | `backup.ok` | `vzdump` with any other severity | job id, else hostname |
   | `replication.failed` | `replication` | job id, else hostname |
   | `node.fenced` | `fencing` | job id, else hostname |
   | `updates.available` | `package-updates` | job id, else hostname |

   The payload holds `type`, `severity`, `hostname`, `jobId` and the notification time. Other types such as `system-mail` and bodies without `fields.type` publish nothing. A body that is not JSON, or whose `fields` is not an object, gets 400.

   To read the cluster on demand, see [Proxmox](proxmox.md).
:::

## Check that it works

1. Use the **Test** button in Uptime Kuma or Proxmox. Marshal answers 202 and publishes no event. A 401 means the token is wrong, a 403 means the scope does not match the source.
2. Pause a monitor or run a backup that fails, then open the web UI. A matching watch makes its task due.
3. List tokens to see the token exists:

   ```sh
   docker exec marshal marshal tokens list
   ```

## Related

- [Event triggers](../tasks/event-triggers.md)
- [Machine tokens](../authentication/machine-tokens.md)
- [Webhook API](../../api/webhooks.md)
- [`webhooks` in marshal.yaml](../../configuration/marshal-yaml/webhooks.md)
- [Event reference](../../reference/events.md)
- [Media requests](media.md) (Seerr webhooks)
