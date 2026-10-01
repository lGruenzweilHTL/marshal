## MODIFIED Requirements

### Requirement: Key naming
All keys in `/config` files SHALL be camelCase and match the .NET options property names; the only exception is `SKILL.md` frontmatter. Options SHALL be validated at startup, before the server accepts requests, and every invalid option SHALL be reported as described in "Startup failure reporting".

#### Scenario: Invalid option
- **WHEN** a required option is missing or invalid
- **THEN** startup fails with a message naming the file and the full key path (e.g. `marshal.yaml` and `embedding.model`)

#### Scenario: Invalid option from the environment
- **WHEN** `MARSHAL__Embedding__Provider=foo` is set and `foo` is not a valid provider
- **THEN** the error names the variable `MARSHAL__Embedding__Provider` instead of `marshal.yaml`

## ADDED Requirements

### Requirement: Startup failure reporting
Startup SHALL check in stages and SHALL run a stage only when every earlier stage found no errors:
1. **Configuration**: `marshal.yaml` parsing, options validation for every section, the literal-secret check of every `/config` file, `risk.yaml` and `satellites.yaml` validation, and secret properties set in configuration.
2. **Database**: database initialization and migration.
3. **Secrets**: resolution of every required secret.
4. **Services**: everything else (MCP servers, tool registry, HTTP listener).

Within the configuration and secrets stages the system SHALL collect every error before failing; the only exception is a `marshal.yaml` that cannot be parsed, which SHALL be reported alone with its line and column. A configuration or secret error SHALL be printed as one line per error naming the file (or environment variable) and key, without the value and without a stack trace. A stack trace SHALL be printed for configuration and secret errors only when the effective default log level is `Debug` or `Trace`. Any other startup failure SHALL print a one-line summary followed by the stack trace. Every startup failure SHALL exit the process with code `1`.

#### Scenario: Several configuration errors at once
- **WHEN** `marshal.yaml` has no `embedding.model` and `risk.yaml` contains invalid YAML
- **THEN** startup prints both errors, each naming its file and key, prints no stack trace, and exits with code `1`

#### Scenario: Later stage waits for a clean configuration
- **WHEN** an option is invalid and a required secret does not resolve
- **THEN** only the option error is reported and the database is not opened or migrated

#### Scenario: All missing secrets at once
- **WHEN** the configuration is valid and both `ha.tokenSecret` and the `anthropic` model's key secret do not resolve
- **THEN** startup prints both references, each naming its key, and exits with code `1`

#### Scenario: Unparseable main file
- **WHEN** `marshal.yaml` contains invalid YAML
- **THEN** startup prints one error naming `marshal.yaml` with the line and column, and exits with code `1`

#### Scenario: Value never printed
- **WHEN** an option is rejected because of its value
- **THEN** the error names the file and key but does not contain the value

#### Scenario: Stack trace at debug level
- **WHEN** `logging.logLevel.default` is `Debug` and an option is invalid
- **THEN** the error lines are followed by the stack trace

#### Scenario: Unexpected failure
- **WHEN** the HTTP port is already in use
- **THEN** startup prints a one-line summary and the stack trace, and exits with code `1`
