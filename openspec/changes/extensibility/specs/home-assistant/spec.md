## ADDED Requirements

### Requirement: HA connection
The system SHALL connect to Home Assistant with `ha.url` and a long-lived token referenced by `ha.tokenSecret` (required secret). It SHALL use HA's REST and websocket APIs for system calls and HA's MCP server (configured as MCP server id `ha`) for model-driven home control.

#### Scenario: Token missing
- **WHEN** `ha.url` is configured and `ha.tokenSecret` does not resolve
- **THEN** startup fails naming the reference

### Requirement: Registry and state cache
The system SHALL cache HA's entity, device, area and floor registries and the set of entities exposed to Assist, and SHALL keep entity states fresh through a websocket subscription. After a reconnect it SHALL reload registries and states.

#### Scenario: Entity renamed
- **WHEN** an entity is renamed in HA
- **THEN** the cache reflects the new name after the registry update event

#### Scenario: Reconnect
- **WHEN** the websocket reconnects after an outage
- **THEN** registries and states are reloaded before the resolver answers again

### Requirement: HA target resolver
The system SHALL register an `IRiskTargetResolver` for the `ha` namespace that expands a call's `name`, `area`, `floor`, `domain` and `device_class` arguments to the concrete exposed entities, each with domain and device class. Unknown names or empty results SHALL be reported as unresolved.

#### Scenario: Area expansion
- **WHEN** `ha.intent__HassTurnOff` is called with `area = hall` and the hall has two lights and a lock
- **THEN** the resolver returns all three entities, and a lock rule makes the call `confirm`

#### Scenario: Non-exposed entity
- **WHEN** a call names an entity that is not exposed to Assist
- **THEN** the resolver reports it as unresolved

### Requirement: Area context without dumps
The context builder SHALL include states of exposed entities in the turn's area and of entities mentioned in the user's text, taken from the cache. It SHALL NOT include all entity states; the model SHALL call `ha.homeassistant__GetLiveContext` for more.

#### Scenario: Kitchen turn
- **WHEN** a turn comes from the kitchen
- **THEN** the system prompt lists kitchen entity states and no entities of other areas that were not mentioned

### Requirement: Timers
If the HA MCP server offers timer intents usable without device context, timers SHALL use them. Otherwise the system SHALL provide `timers.start(duration, name?)`, `timers.cancel(name?)` and `timers.status()` (safe), which call HA's intent handling with the turn's device id, and SHALL hide them in turns without a device.

#### Scenario: Timer from a satellite
- **WHEN** a voice turn from a satellite asks for a 10-minute timer and the fallback tools are active
- **THEN** HA starts a timer bound to that satellite's device

#### Scenario: No device
- **WHEN** a web chat turn has no device id
- **THEN** no `timers_*` tool is visible

### Requirement: Instance time zone from HA
When `instance.timeZone` is not configured, the instance time zone SHALL be HA's configured time zone.

#### Scenario: HA time zone
- **WHEN** HA reports `Europe/Vienna` and `instance.timeZone` is unset
- **THEN** new users default to `Europe/Vienna`
