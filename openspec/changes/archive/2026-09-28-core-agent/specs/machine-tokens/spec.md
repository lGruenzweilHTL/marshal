## ADDED Requirements

### Requirement: Scoped API tokens
Machine clients SHALL authenticate with API tokens (`Authorization: Bearer`). Each token SHALL have an owner, a name, a set of scopes (e.g. `conversation:write`, `tasks:read`, `admin`), a kind (`standard` | `executor_callback`), optional expiry and a revocation time. Only a hash of the token SHALL be stored; the plaintext SHALL be shown once at creation.

#### Scenario: Token hash only
- **WHEN** a token is created
- **THEN** the database stores its hash, and the plaintext is returned only in the creation output

#### Scenario: Revoked token
- **WHEN** a revoked or expired token is used
- **THEN** the request is rejected with 401

### Requirement: CLI token creation
The container SHALL provide an admin CLI command (`marshal tokens create --name <name> --scope <scope>...`, plus `list` and `revoke`) that works without a web login. Tokens created this way SHALL be owned by the instance owner.

#### Scenario: Create a token for Home Assistant
- **WHEN** an operator runs `marshal tokens create --name ha --scope conversation:write`
- **THEN** a token with exactly that scope is created and printed once

### Requirement: Token principals use the same policies
Token principals SHALL be authorized by the same ASP.NET policies as all other principals, based on their scopes.

#### Scenario: Scope check
- **WHEN** a token with only `tasks:read` calls an endpoint requiring `conversation:write`
- **THEN** the response is 403
