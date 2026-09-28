# configuration Specification

## Purpose
TBD - created by archiving change core-agent. Update Purpose after archive.
## Requirements
### Requirement: Configuration sources and layering
Configuration SHALL be layered, low to high: built-in defaults → `/config/marshal.yaml` → environment variables with the exact prefix `MARSHAL__` (e.g. `MARSHAL__Models__Fast__Provider`). Variables starting with `MARSHAL_SECRET_` SHALL NOT enter `IConfiguration`.

#### Scenario: Environment overrides YAML
- **WHEN** `marshal.yaml` sets `http.pathBase: /a` and `MARSHAL__Http__PathBase=/b` is set
- **THEN** the effective base path is `/b`

#### Scenario: Secret variables stay out of configuration
- **WHEN** `MARSHAL_SECRET_MOONRAKER_VORON` is set
- **THEN** no configuration key contains its value

### Requirement: Key naming
All keys in `/config` files SHALL be camelCase and match the .NET options property names; the only exception is `SKILL.md` frontmatter. Options SHALL be validated at startup (`ValidateOnStart`).

#### Scenario: Invalid option
- **WHEN** a required option is missing or invalid
- **THEN** startup fails with a message naming the key

### Requirement: Base path
The HTTP base path SHALL be configurable (`http.pathBase`) and SHALL prefix every route, including the versioned API. The API version SHALL never be configurable.

#### Scenario: Reverse proxy base path
- **WHEN** `http.pathBase` is `/marshal`
- **THEN** the conversation endpoint is served at `/marshal/api/v1/conversation/turns`

### Requirement: Hot reload
Files under `/config` SHALL be watched. Risk rules and satellite profiles (and, in later changes, skills, templates and MCP servers) SHALL reload without restart. Model and auth settings SHALL require a restart. A reload that fails validation SHALL keep the previous configuration and log the error.

#### Scenario: Broken reload
- **WHEN** `risk.yaml` is saved with invalid YAML
- **THEN** the previous rules stay active and an error is logged

### Requirement: No literal secrets in configuration
On startup and on every reload the system SHALL check every `/config` file and fail (or keep the previous config on reload) when:
- a key ending in `Secret` has a value not matching `^[a-z][a-z0-9_]{0,63}$`;
- a key's last camelCase word is `password`, `passphrase`, `token`, `credential` or `credentials`, or its last two words are `api key`, `private key` or `access key`;
- any value looks like a credential: PEM header, JWT (`eyJ`), `sk-`, `sk-ant-`, `ghp_`, `xox?-`, a hex string of 32+ characters, a base64 / base64url string of 32+ characters, or another long high-entropy string.

Options properties marked `[Secret]` SHALL only be bound from `ISecretStore`.

#### Scenario: Literal API key
- **WHEN** a config file contains `apiKey: abc`
- **THEN** startup fails naming the file and key, without printing the value

#### Scenario: Harmless token word
- **WHEN** a config file contains `maxTokens: 4096`
- **THEN** startup succeeds

#### Scenario: Hex key in a reference
- **WHEN** a config file contains `apiKeySecret: 0a1b2c3d4e5f60718293a4b5c6d7e8f9` (32 hex characters)
- **THEN** startup fails because the value scan matches, even though the name pattern would allow it

