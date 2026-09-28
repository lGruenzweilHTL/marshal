## ADDED Requirements

### Requirement: Cookie clients on the turn endpoint
`POST /api/v1/conversation/turns` SHALL accept cookie principals. For cookie principals `conversationId` SHALL be optional: omitted creates a new session with source `web`; otherwise it SHALL be Marshal's own session id and SHALL belong to the current user. The final event SHALL return Marshal's session id.

#### Scenario: Foreign session id
- **WHEN** a cookie user sends another user's session id
- **THEN** the response is 404 and no run starts

#### Scenario: New web session
- **WHEN** a cookie user sends a turn without `conversationId`
- **THEN** a session with source `web` is created and its id is in the final event

### Requirement: Role-derived scopes
A cookie principal SHALL carry the scopes its roles grant: `user` → all non-admin scopes, `admin` → all scopes. Cookie and token principals SHALL be authorized by the same policies.

#### Scenario: User role cannot use admin endpoints
- **WHEN** a cookie user with role `user` calls an endpoint requiring `admin`
- **THEN** the response is 403
