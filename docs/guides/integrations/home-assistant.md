# Connect Home Assistant

Connect Marshal to Home Assistant (HA) so it reads your home's state, keeps a live cache, and adds the timer, places, energy and context features described below.

Home control by the model goes through HA's MCP server: see [Home Assistant MCP server](home-assistant-mcp.md). This page covers the connection itself.

## Before you start

- A running Home Assistant that Marshal's container can reach over the network.
- A long-lived access token of an **administrator** user. The list of entities exposed to Assist needs admin rights, and so does `places.create_here`.
- In HA, the entities Marshal may see and control are exposed to Assist (Settings, Voice assistants, Expose). Marshal never sees other entities.
- A way to provide the token as a secret: a file `/run/secrets/ha_token` or the variable `MARSHAL_SECRET_HA_TOKEN`. See [secrets](../secrets.md).

## Steps

::: steps
1. **Create the token**

   In HA, open your profile, Security, Long-lived access tokens, and create one named `marshal`. Copy it once.

2. **Store the token as a secret**

   ```sh
   printf '%s' '<token>' > ./secrets/ha_token
   ```

   Mount it into the container at `/run/secrets/ha_token`, or pass it as `MARSHAL_SECRET_HA_TOKEN`. `/config` never holds the value.

3. **Configure the connection**

   In `/config/marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   ha:
     url: http://homeassistant.local:8123
     tokenSecret: ha_token
   ```

   `ha.tokenSecret` names the secret. If `ha.url` is set and `ha.tokenSecret` is missing or does not resolve, startup fails and names the reference. See [`ha` in the integration sections](../../configuration/marshal-yaml/integrations.md#ha-home-assistant) for the keys.

   Restart Marshal after changing `marshal.yaml`.

4. **Add the MCP server**

   Copy `deploy/config.example/mcp/ha.yaml` to `/config/mcp/ha.yaml`. It uses the same secret. See [Home Assistant MCP server](home-assistant-mcp.md).
:::

## What the connection gives you

- **Registry and state cache.** Marshal caches the entity, device, area and floor registries and the set of entities exposed to Assist. It keeps states fresh through a websocket subscription. After a reconnect it reloads registries and states before it answers again. HA has no event for exposure changes, so the registries are also refreshed every five minutes.
- **Target resolver.** Before a `ha.*` call runs, Marshal expands `name`, `area`, `floor`, `domain` and `device_class` to the concrete exposed entities. [Risk rules](../risk-rules.md) match on the resolved domains and device classes. A name that is not exposed, or an empty result, is reported as unresolved.
- **Area context.** A turn from the kitchen gets the states of exposed kitchen entities, and of entities named in the user's text, in its prompt. It never gets all states. For more the model calls `ha.homeassistant__GetLiveContext`.
- **Time zone.** When `instance.timeZone` is unset, the instance time zone is HA's configured time zone.
- **Voice and push channels.** Notifications use HA for announcements and push. See [notifications](../notifications.md).

## Tools this integration adds

All tools are built in and hidden while `ha.url` is unset. The `ha.*` tools from the MCP server are listed on [their own page](home-assistant-mcp.md).

| Tool | Tier | What it does | Hidden when |
|---|---|---|---|
| `timers.start(duration, name?)` | safe | Starts a timer on the device the user talks to. `duration` such as `10m`, `1h30m`, `90s` or ISO 8601. | the turn has no device, or HA's MCP server offers `HassStartTimer` |
| `timers.cancel(name?)` | safe | Cancels a timer by name, or the only one. | same |
| `timers.status()` | safe | Lists the timers on the device. | same |
| `places.find(query?)` | safe | Lists HA zones with radius, whether you are inside, and the state value your person entity takes there. | no HA connection |
| `places.create_here(name, radius?)` | confirm | Creates a zone at your person entity's GPS position. Radius 50 to 1000 m, default 150. | no HA connection, or no person entity configured for the owner |
| `energy.usage(period, from?, to?, compareWith?)` | safe | Energy for a period from the energy dashboard. | no HA connection, or the energy dashboard has no sources |

The model sees these as `timers_start`, `places_find` and so on. Calendar tools also use this connection: see [calendar](calendar.md).

### Timers

Timers need a device, so they work in voice turns from a satellite and not in web chat. They call HA's intent handling with the turn's device id, so the timer rings at that satellite. If HA's MCP server ever offers timer intents usable without device context, Marshal uses those and hides `timers.*`.

### Places

`places.find` and `places.create_here` only read the owner's own person entity, never another person's. `places.create_here` fails without calling HA when the person entity has no GPS position, or when a zone with the same name (case-insensitive) exists. HA rejects the call if the token's user is not an administrator, and you see HA's message. Passive zones are listed and marked passive, because a person entity never takes their name as state.

### Energy

`period` is one of `today`, `yesterday`, `week` (Monday 00:00 until now), `month`, `last_week`, `last_month` or `range` (`from` and `to` as local dates, `to` inclusive). Boundaries use your time zone. The result has the sources your dashboard configures: grid import and export, solar, battery charge and discharge, gas, water, household consumption, cost and compensation when HA records cost statistics, and up to five top devices. Sources the dashboard does not configure are absent, not zero. `compareWith: previous` adds the previous period of equal elapsed length. Open periods end at the current full hour, because HA's long-term statistics are hourly.

## Limits

- One Home Assistant instance per Marshal instance.
- Marshal sees only entities exposed to Assist.
- While the websocket is down, `places.find` and `energy.usage` fail with an error saying Home Assistant is unavailable.

## Check that it works

1. Open `/health`. The Home Assistant entry is healthy.
2. In web chat ask "which lights are on in the kitchen?" The answer comes from the cache or from `ha_homeassistant__GetLiveContext`.
3. From a satellite say "set a timer for ten minutes". A timer starts on that satellite.
4. Ask "where is the Hornbach zone?" The model calls `places_find`.

## Troubleshooting

- **Startup fails naming `ha.tokenSecret (ha_token)`.** The secret does not resolve. Check the file name or variable.
- **Entities are unresolved.** The entity is not exposed to Assist in HA.
- **`places.create_here` is missing.** The owner has no person entity set up as a notification target.

More in [troubleshooting](../troubleshooting.md).

## Related

- [Home Assistant MCP server](home-assistant-mcp.md)
- [Calendar](calendar.md)
- [Connect Home Assistant (getting started)](../../getting-started/connect-home-assistant.md)
- [Risk rules](../risk-rules.md)
- [Integration sections reference](../../configuration/marshal-yaml/integrations.md)
- [Tools reference](../../reference/tools.md)
