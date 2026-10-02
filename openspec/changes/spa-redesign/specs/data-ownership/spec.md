## ADDED Requirements

### Requirement: Unexpected failures as ProblemDetails
An unhandled exception in an API request SHALL be answered with status 500 and a ProblemDetails body carrying the code `server.unexpected`, a generic message and the request's trace id. The body SHALL NOT contain the exception message, type or stack trace. The exception SHALL be logged with the same trace id.

#### Scenario: Unhandled exception
- **WHEN** an endpoint throws an exception that nothing handles
- **THEN** the response is 500 ProblemDetails with code `server.unexpected` and a `traceId`, and the server log entry for the exception carries the same trace id

#### Scenario: No internals exposed
- **WHEN** an unhandled exception's message contains a file path
- **THEN** the response body does not contain that message
