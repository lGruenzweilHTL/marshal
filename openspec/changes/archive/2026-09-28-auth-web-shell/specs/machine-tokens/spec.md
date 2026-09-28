## ADDED Requirements

### Requirement: Token management API and view
Users SHALL list, create and revoke their own API tokens via `/api/v1/me/tokens` and the security view. A user SHALL only grant scopes they hold themselves. The plaintext SHALL be shown once after creation. Every create and revoke SHALL be audited with the user as actor.

#### Scenario: Scope escalation attempt
- **WHEN** a user with role `user` creates a token with scope `admin`
- **THEN** the request is rejected

#### Scenario: Revoke
- **WHEN** a user revokes a token
- **THEN** the next request with that token gets 401 and an audit event `token.revoke` exists

### Requirement: Executor callback tokens are not user-managed
Tokens of kind `executor_callback` SHALL be listed read-only and SHALL NOT be creatable through the API.

#### Scenario: Create callback token via API
- **WHEN** a user tries to create a token of kind `executor_callback`
- **THEN** the request is rejected
