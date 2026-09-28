## ADDED Requirements

### Requirement: Configured login providers
The system SHALL support login providers configured as a list: `oidc` (authority, client id, `clientCredentialSecret`, scopes, claim mappings for subject, name and groups) and `local`. Several providers MAY be active at the same time. `GET /api/v1/auth/providers` SHALL list the active providers without authentication.

#### Scenario: Two providers
- **WHEN** config lists one `oidc` and one `local` provider
- **THEN** `/api/v1/auth/providers` returns both

### Requirement: BFF cookie session
For `oidc`, the backend SHALL act as confidential client using authorization code + PKCE and keep IdP tokens server-side. After login the browser SHALL receive only an HttpOnly, Secure, `SameSite=Lax` session cookie. The SPA SHALL start login via `/api/v1/auth/login/{provider}` and log out via `/api/v1/auth/logout`. Auth protocol callbacks (`/signin-oidc`, `/signout-callback-oidc`) SHALL be unversioned.

#### Scenario: Cookie flags
- **WHEN** an OIDC login completes
- **THEN** the response sets a cookie with HttpOnly, Secure and SameSite=Lax, and no IdP token reaches the browser

#### Scenario: Deep link after login
- **WHEN** a logged-in user opens a deep link from a push notification in the browser
- **THEN** the page loads authenticated

### Requirement: Antiforgery
All state-changing endpoints called with a cookie principal SHALL require a valid antiforgery token and SHALL NOT be reachable with GET.

#### Scenario: Missing antiforgery token
- **WHEN** a cookie-authenticated POST arrives without an antiforgery token
- **THEN** the response is 400 and nothing changes

### Requirement: One Identity user store
All human users SHALL be ASP.NET Core Identity users whose key is exposed everywhere else as `OwnerId`. OIDC logins SHALL be stored as external logins (provider + subject) and matched by subject, never by email. The same user MAY have an OIDC login and local credentials (break-glass access).

#### Scenario: Email changed at the IdP
- **WHEN** a user's email changes at the IdP but the subject stays the same
- **THEN** the login maps to the same Marshal user

### Requirement: Local provider
The `local` provider SHALL support passkeys and password fallback via ASP.NET Core Identity. When no user exists, a first-run setup SHALL create the first user as admin; afterwards the setup SHALL be unavailable.

#### Scenario: First run
- **WHEN** the instance has no users and `local` is active
- **THEN** the setup page creates the first user with role `admin`

#### Scenario: Setup after first user
- **WHEN** a user already exists
- **THEN** the setup endpoint returns 404

### Requirement: Role provisioning from claims
OIDC users SHALL get roles from group claims via configured mappings (e.g. `marshal-admins` → `admin`, `marshal-users` → `user`), re-synced on every login. A login without any mapped group SHALL be denied.

#### Scenario: Unmapped user
- **WHEN** an OIDC user without a mapped group logs in
- **THEN** login is denied and no user is created

#### Scenario: Group removed
- **WHEN** an admin's `marshal-admins` group is removed at the IdP and they log in again
- **THEN** their `admin` role is removed

### Requirement: Current user endpoint
`GET /api/v1/me` SHALL return the current user's id, display name, roles and scopes.

#### Scenario: Anonymous call
- **WHEN** `/api/v1/me` is called without a session
- **THEN** the response is 401
