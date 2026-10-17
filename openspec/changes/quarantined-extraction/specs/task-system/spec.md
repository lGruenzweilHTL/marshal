## MODIFIED Requirements

### Requirement: Durable tasks
Tasks SHALL be stored in SQLite with owner, parent, `created_by` (`user` | `task` | `system`), kind (`agent` | `system`), goal, template, tool policy (agent tasks: a list of tool id globs), optional project id, optional origin satellite, trigger, status (`pending`, `running`, `waiting`, `done`, `failed`, `cancelled`), next run time, notes, budget and spend, deliveries, optional quiet-hours override, optional name (unique per owner), result (`speech`, `body`) and depth. Tasks SHALL survive restarts.

#### Scenario: Restart
- **WHEN** Marshal restarts while a task is `waiting` with a future `at` trigger
- **THEN** the task runs at its trigger time after the restart

#### Scenario: Duplicate name
- **WHEN** a second task named `briefing` is created for the same owner
- **THEN** creation fails with a validation error

## ADDED Requirements

### Requirement: Task tool policy
When an agent task is created, the system SHALL freeze its tool policy: the `tools` argument if given, otherwise the tools of the template's allowlist whose namespace is a core namespace or was loaded in the creating session. A `tools` argument SHALL only contain globs that match tools of the template's allowlist; others SHALL fail validation. A task created without a session (web UI, configuration) SHALL get the template's allowlist. Every run of the task, and every subagent it starts, SHALL see and call only tools inside the policy. `tasks.update` SHALL NOT change the policy; only a user action in the web UI SHALL, and the change SHALL be audited. A task created before this requirement SHALL behave as if its policy were the template's allowlist. The confirmation text of a task creation SHALL list the policy.

#### Scenario: Derived policy
- **WHEN** a turn that loaded `printer` creates a task with template `default` without `tools`
- **THEN** the task's policy contains the core namespaces' tools and the printer tools, and its runs cannot see mail tools

#### Scenario: Narrowing argument
- **WHEN** `tasks_create` is called with `tools: [printer.status, notifications.notify]`
- **THEN** the task's runs see only those two tools (plus nothing else)

#### Scenario: Widening rejected
- **WHEN** `tasks_create` is called with `tools: [mail.*]` and the template does not allow mail tools
- **THEN** the call fails with a validation error and no task is created

#### Scenario: Model cannot widen later
- **WHEN** a run calls `tasks_update` with a `tools` argument
- **THEN** the call fails with a validation error naming `tools`
