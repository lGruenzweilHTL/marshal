## ADDED Requirements

### Requirement: Trace context propagation
Every request to an MCP server SHALL carry the W3C trace context in `_meta` (`traceparent`, and `tracestate` when present) of an MCP client span for the request that is a child of the current `execute_tool` span, so that a tracing server's spans join the run's trace. Requests made outside a run (tool listing, discovery) SHALL carry the context of the current span when there is one.

#### Scenario: Tool call carries traceparent
- **WHEN** a run calls `ha_HassTurnOn`
- **THEN** the MCP request's `_meta` contains a `traceparent` whose trace id equals the run's trace id and whose parent is the request's MCP client span, a child of the tool call's span
