## ADDED Requirements

### Requirement: Pending input requests
When an MCP server answers a tool call with an input request, the system SHALL create a pending input request (owner, server, tool, frozen arguments, the server's request in form or URL mode, run, task if any, nonce) with the approval TTL, and SHALL NOT pass the request's content to the model. The model SHALL receive a tool result saying the server needs input from the user in the web UI. The review queue SHALL show form requests as a form generated from the requested schema and URL requests as a link plus a "Done" action. Only a user action in the web UI SHALL answer; the server SHALL then repeat the frozen call once with the answer and handle the result like an approved action's result. In background runs the run SHALL suspend as for approvals. A push notification with a link to the request SHALL be sent; it SHALL NOT carry answer actions.

#### Scenario: Form request in a web turn
- **WHEN** a web turn's `paperless_upload` call returns a form request for a correspondent
- **THEN** the chat shows the form, the model's tool result only says input is needed in the web UI, and after the user submits, the call is repeated with the answer and its result appears in the conversation

#### Scenario: URL request from a voice turn
- **WHEN** a voice turn's call returns a URL request to log in
- **THEN** the spoken reply says to continue in the web UI, a push with a link is sent, and the URL is never part of the model context

#### Scenario: Expired request
- **WHEN** nobody answers within the TTL
- **THEN** the request expires, nothing is sent to the server, and a suspended task resumes with `rejected: expired`
