# `suggestions`

Thresholds of the acceptance estimate, the expiry of suggestions and the settings of each suggestion detector. Suggestions run only for owners with "help Marshal learn" on.

## Example

```yaml
suggestions:
  explore: 3
  minAcceptance: 0.3
  pushAcceptance: 0.6
  expiry: "06:00:00"
  detectors:
    window_rain:
      minProbability: 70
      weatherEntity: weather.home
    energy_anomaly:
      factor: 2.0
    print_done_away:
      cooldown: "06:00:00"
    maintenance_counter:
      enabled: false
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `suggestions.explore` | integer | `3` | The first this many candidates of a detector go to the Inbox whatever the estimate. |
| `suggestions.minAcceptance` | number | `0.3` | Below this estimate a candidate is dropped. |
| `suggestions.pushAcceptance` | number | `0.6` | At or above this estimate a candidate is pushed with priority `normal`. Between the two thresholds it goes to the Inbox with priority `low`. |
| `suggestions.expiry` | duration | `06:00:00` | An unanswered suggestion expires after this, or earlier when its condition no longer holds. Expired suggestions do not change the counts. |
| `suggestions.detectors.<id>.enabled` | boolean | `true` | `false` turns the detector off for every owner. |
| `suggestions.detectors.<id>.cooldown` | duration | `12:00:00` | A candidate with the same subject (the same window, device or printer) sent this recently is dropped. |
| `suggestions.detectors.window_rain.minProbability` | integer | `60` | Precipitation probability in percent that counts as rain. |
| `suggestions.detectors.window_rain.weatherEntity` | string | first `weather.*` entity | The weather entity whose forecast is read. |
| `suggestions.detectors.energy_anomaly.factor` | number | `1.5` | A device is unusual when yesterday's consumption is at least this times its median of the previous 14 days. |

The estimate is (accepted + 1) / (accepted + dismissed + 2) per owner and detector. Owners mute detectors and reset counts in **Preferences**, not here.

### Detectors

| Id | Needs | Suggests |
|---|---|---|
| `window_rain` | `ha`, a weather entity, `binary_sensor` entities with device class `window` or `door` | Closing an open window when rain is likely within the hour (hourly forecast; else today's daily forecast). |
| `energy_anomaly` | `ha` with devices on the energy dashboard | Checking a device that used unusually much energy yesterday. Devices with less than 14 days of history are skipped. |
| `print_done_away` | `ha`, `printers`, the owner's `person` entity | Switching off a printer that finished while you are away. Offers the action only with `printers[].powerEntity`. |
| `maintenance_counter` | `printers`, or `ha` sensors with state class `total_increasing` that count runtime or cycles | A [maintenance item](maintenance.md) for a counter that has none, with a YAML snippet. |

## Validation

- `suggestions.explore cannot be negative.`
- `suggestions.minAcceptance and suggestions.pushAcceptance must be between 0 and 1, minAcceptance not above pushAcceptance.`
- `suggestions.expiry must be positive.`
- `suggestions.detectors.<id>: a detector id must match [a-z][a-z0-9_]*.`
- `suggestions.detectors.<id>.cooldown cannot be negative.`
- `suggestions.detectors.<id>.minProbability must be between 0 and 100.`
- `suggestions.detectors.<id>.factor must be greater than 1.`

## Related

- [Get suggestions](../../guides/suggestions.md)
- [maintenance](maintenance.md)
- [notifications](notifications.md)
