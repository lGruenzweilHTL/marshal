# Spec Delta

## ADDED Requirements

### Requirement: OpenAPI document content
Each operation in a versioned OpenAPI document SHALL have its route below the version base as its summary (for `POST /api/v1/conversation/turns` the summary is `/conversation/turns`) and SHALL describe what it does in its description. The document SHALL declare the security schemes `apiToken` (HTTP bearer, a machine token) and `session` (API key in the cookie `marshal_session`). Each operation SHALL carry the security requirements its endpoint enforces:
- an anonymous endpoint SHALL have no security requirement;
- an endpoint limited to people SHALL require `session`;
- an endpoint that requires a scope SHALL accept `apiToken` with that scope or `session`;
- an endpoint for people or `admin` tokens SHALL accept `session` or `apiToken` with `admin`;
- an endpoint that requires only an authenticated caller SHALL accept `apiToken` or `session`;
- an endpoint limited to machine tokens SHALL require `apiToken`.
Where an endpoint combines several policies, it SHALL accept only credentials that every policy accepts, with the union of their scopes. Generating a document SHALL fail when an endpoint uses an authorization policy without a documented credential mapping.

#### Scenario: Operation named by route
- **WHEN** a client reads `/openapi/v1.json`
- **THEN** the operation `CreateConversationTurn` has the summary `/conversation/turns` and a description of the turn

#### Scenario: Scope-protected endpoint
- **WHEN** a client reads the operation `CreateConversationTurn`
- **THEN** its security accepts `apiToken` with the scope `conversation:write`, or `session`

#### Scenario: People-only endpoint
- **WHEN** a client reads an operation of an endpoint that requires a browser session, such as `ListApiTokens`
- **THEN** its security requires `session` only

#### Scenario: Anonymous endpoint
- **WHEN** a client reads the operation `ListLoginProviders`
- **THEN** it has no security requirement

#### Scenario: Unknown policy
- **WHEN** an endpoint requires an authorization policy that has no credential mapping and the document is generated
- **THEN** generation fails with an error naming the policy and the endpoint
