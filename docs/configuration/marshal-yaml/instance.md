# `instance`, `paths`, `logging`, `telemetry`

Instance-wide settings: the default time zone, volume locations, log levels and trace export.

## Example

```yaml
instance:
  timeZone: Europe/Vienna

telemetry:
  otlpEndpoint: "http://otel-collector:4317"
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `instance.timeZone` | string (IANA id) | unset | Time zone new users start with. Unset: Home Assistant's time zone when `ha` is configured, otherwise UTC. |
| `paths.config` | string | `/config` | Config directory. Set it with `MARSHAL__Paths__Config`: Marshal reads that variable before `marshal.yaml` exists. |
| `paths.data` | string | `/data` | Database, Data Protection keys, backups, artifacts, snapshots, notes. |
| `paths.secrets` | string | `/run/secrets` | Directory of Docker secret files. |
| `logging.logLevel.default` | string | .NET default | Standard .NET log level (`Trace`, `Debug`, `Information`, ...). `Debug` and `Trace` also print stack traces for configuration errors. The `logging` section is the standard .NET `Logging` configuration. |
| `telemetry.otlpEndpoint` | URL | unset | OTLP gRPC endpoint for traces and metrics. Unset: no export. |

`paths.*` are meant to be set through `MARSHAL__Paths__*` variables, not in `marshal.yaml`.

## Validation

- `instance.timeZone is not an IANA time zone.` when the value is not a known IANA id.

## Related

- [Index](../index.md)
- [Observability](../../guides/observability.md)
- [Data directory](../../reference/data-directory.md)
