# Try Marshal in demo mode

Run Marshal with only a model configured and still see every view and the main tool flows, with simulated integrations and sample history.

## Before you start

- A running Marshal instance with a working `models` section. Demo mode does not replace the model; startup fails without one, as it does without demo mode.
- An embedding model, if you want the sample memory. Without a reachable embedding model the memory part of the sample data is skipped.

## Steps

### 1. Turn demo mode on

Add the `demo` section to `/config/marshal.yaml`:

```yaml
demo:
  enabled: true
```

### 2. Restart Marshal

```sh
docker compose restart marshal
```

The log warns that demo mode is on. On the first start with demo mode on, Marshal seeds the sample data for the instance owner. If the owner does not exist yet, the data is seeded right after the first-run setup creates it.

### 3. Explore

Each of these integrations is simulated when it is not configured. A configured integration stays real.

| Integration | What the simulation has |
|---|---|
| Home Assistant | Areas, lights, climate, sensors, zones, energy, timers, music players and a calendar. Service calls change the simulated states. Without an MCP server `ha`, the home control tools `ha.intent__HassTurnOn`, `ha.intent__HassTurnOff`, `ha.intent__HassLightSet`, `ha.intent__HassClimateSetTemperature` and `ha.homeassistant__GetLiveContext` work on the same states. |
| Printers | One printer, `voron`, in the middle of a print that advances over time, with snapshots and a webcam image. |
| Mail | A mail account `demo` with sample messages. Drafts are kept until the next restart. There is no send path. |
| FreshRSS | Feeds in two categories, with articles and their full text. |
| Notes | A small collection of Markdown notes, indexed like a real notes repository. |
| Web search | Fixed search results for every query. Result pages are never opened, and no search engine is contacted. |

The simulated tools have the same ids, risk tiers and guard rules as the real ones. For example, cancelling the print still needs your confirmation.

The sample history belongs to the instance owner and is dated relative to the seed time:

- conversations on the web and by voice from the `kitchen` and `office` satellites, including a turn that moved to the background and its result
- runs with their steps
- a `morning` task with past results, and a task that waits for your approval to cancel the print
- notifications in the Inbox
- memory chunks with edges, one closed chunk, a chunk with an earlier version and its valid time, and one open duplicate proposal in the audit

Simulated state, such as a light you turned on or a draft, starts over on each restart.

## Hide the badge for screenshots

The navigation shows a "Demo" badge while demo mode is on. Hide it with:

```yaml
demo:
  enabled: true
  showBadge: false
```

The system view still marks every simulated integration.

## Remove the sample data

```sh
docker compose exec marshal marshal demo reset
```

This deletes exactly the seeded rows. Tasks, conversations and memory that you or the agent created during the demo stay. A restart does not seed again after a reset. To seed again, run:

```sh
docker compose exec marshal marshal demo seed
```

## Turn demo mode off

Set `demo.enabled: false` or remove the section, and restart. The sample data stays until you run `marshal demo reset`.

> [!WARNING]
> Demo mode on a real instance fills every unconfigured integration with simulated data. Turn it on only to try Marshal or to take screenshots.

## Related

- [`demo` configuration](../configuration/marshal-yaml/demo.md)
- [`marshal demo`](../reference/cli.md#marshal-demo)
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
