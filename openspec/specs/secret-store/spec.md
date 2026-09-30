# secret-store Specification

## Purpose
How secrets are stored and found by name (environment, files or the encrypted database), never in configuration.
## Requirements
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

### Requirement: Secret management API and view
Users SHALL list, create, replace and delete DB secrets through `/api/v1/me/secrets` (own secrets) and `/api/v1/secrets/instance` (instance secrets, `admin` only). Values SHALL be write-only: no endpoint SHALL return a value. The list SHALL show name, scope (instance / user) and last update. Every change SHALL be audited without the value.

#### Scenario: Write-only
- **WHEN** a secret is created and then fetched through any endpoint
- **THEN** the response contains no value

#### Scenario: Non-admin instance secret
- **WHEN** a user without `admin` creates an instance secret
- **THEN** the response is 403

### Requirement: Environment secrets and unresolved references
The secrets view SHALL list secrets provided by environment or `/run/secrets` read-only with their source and no value, and SHALL flag every secret reference from `/config` or user-owned rows that does not resolve.

#### Scenario: Unresolved reference
- **WHEN** `/config/marshal.yaml` references `freshrss_api` and no source has it
- **THEN** the secrets view flags `freshrss_api` as unresolved and names where it is referenced

