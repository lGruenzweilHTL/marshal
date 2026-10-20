## MODIFIED Requirements

### Requirement: Content Security Policy
The SPA SHALL be served with a CSP that allows scripts and styles only from the same origin, images only from `'self'` and `data:`, and frames only from `'self'`. The MCP app endpoint SHALL be served with its own CSP that allows inline scripts and styles, images from `data:` only, no connections, no frames, and framing only by Marshal's own origin.

#### Scenario: External image blocked
- **WHEN** a page tries to load an image from another origin
- **THEN** the browser blocks it by CSP

#### Scenario: External frame blocked
- **WHEN** a page tries to frame another origin
- **THEN** the browser blocks it by CSP

#### Scenario: App cannot connect
- **WHEN** a script in an MCP app tries `fetch("https://example.com")`
- **THEN** the browser blocks it by the app's CSP
