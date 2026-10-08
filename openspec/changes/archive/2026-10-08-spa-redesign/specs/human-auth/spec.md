## MODIFIED Requirements

### Requirement: Antiforgery
All state-changing endpoints called with a cookie principal SHALL require a valid antiforgery token and SHALL NOT be reachable with GET. Browser sessions and their antiforgery tokens SHALL require HTTPS; a token request or a state-changing browser request over plain HTTP SHALL fail with the `auth.https_required` problem.

#### Scenario: Missing antiforgery token
- **WHEN** a cookie-authenticated POST arrives without an antiforgery token
- **THEN** the response is 400 and nothing changes

#### Scenario: Token requested over plain HTTP
- **WHEN** `GET /api/v1/auth/antiforgery` arrives over plain HTTP (no TLS and no trusted forwarded `https` scheme)
- **THEN** the response is 400 ProblemDetails with code `auth.https_required`, not a server error
