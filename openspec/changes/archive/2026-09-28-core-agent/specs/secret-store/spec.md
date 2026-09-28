## ADDED Requirements

### Requirement: Secrets by name
All secret values SHALL be obtained through `ISecretStore.GetAsync(SecretName, ct)`, returning `Result<string>`. Configuration and data rows SHALL hold only secret names. A secret name SHALL match `^[a-z][a-z0-9_]{0,63}$`.

#### Scenario: Missing secret
- **WHEN** a name resolves in no source
- **THEN** the store returns a failed `Result` with code `secret.not_found`

### Requirement: Environment and file source
The store SHALL read `MARSHAL_SECRET_<NAME>` environment variables (name upper-cased) and files `/run/secrets/<name>`. This source is read-only.

#### Scenario: Docker secret file
- **WHEN** `/run/secrets/moonraker_voron` exists
- **THEN** `GetAsync("moonraker_voron")` returns its content without trailing newline

### Requirement: Database source
The store SHALL keep UI-managed secrets in the `Secret` table, encrypted with ASP.NET Core Data Protection (keys in `/data/keys`). `owner_id` null SHALL mean instance-scoped. Uniqueness SHALL be enforced by two partial indexes: `unique(name) where owner_id is null` and `unique(owner_id, name) where owner_id is not null`. Values SHALL never be returned by any API.

#### Scenario: Ciphertext at rest
- **WHEN** a DB secret is stored
- **THEN** the `ciphertext` column does not contain the plaintext

#### Scenario: Duplicate instance secret
- **WHEN** two instance secrets with the same name are inserted
- **THEN** the second insert fails

### Requirement: Lookup order
Names referenced from `/config` SHALL resolve environment / files first, then instance DB rows, never a user's rows. Names referenced from user-owned rows SHALL resolve environment / files, then the owner's DB rows, then instance DB rows.

#### Scenario: User secret overrides instance secret
- **WHEN** both an instance row and owner A's row named `freshrss` exist and a row owned by A references `freshrss`
- **THEN** owner A's value is returned

#### Scenario: Config never reads user rows
- **WHEN** only owner A has a row named `ha_token` and `/config` references `ha_token`
- **THEN** the lookup fails

### Requirement: Required secrets
Secrets needed at startup (model API keys, OIDC client secret, HA token) SHALL fail startup when unresolved. Other unresolved references SHALL only produce a health warning.

#### Scenario: Missing model key
- **WHEN** an `anthropic` model is configured and its key secret does not resolve
- **THEN** startup fails naming the secret reference
