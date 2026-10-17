# Data ownership

This page explains how Marshal keeps each user's data apart, even though an instance has one user today.

## The problem

Marshal runs with one user per instance for now. It is tempting to read "the user" from a global and skip owner checks. That works until a second person in the household gets an account, and then every query, every tool and every background job leaks. Retrofitting ownership later means touching every table and every line of data access. A single missed `WHERE` clause shows your mail summaries to someone else.

## The rule

**Every user-owned row has `owner_id`, and all data access goes through owner-scoped queries. Never read "the user" from a global.**

## How it works

### An owner on every row

Every user-owned table, child tables included, has a non-null `owner_id` of type `OwnerId`. A run step carries the owner of its run; a chunk version carries the owner of its chunk. The exceptions:

- ASP.NET Identity tables (they hold the users themselves)
- `UserPreferences`, keyed by the user's id
- `Secret`, where a null owner means an instance-scoped secret
- MCP tool pins, which are instance configuration and have no owner column; the MCP client reads them regardless of the execution context

A null owner means "instance-scoped" only on `Secret`. Shared household data, when it arrives, will use an explicit scope column instead of a missing owner.

### The execution context

Every run carries an execution context: `{ OwnerId, Source, InputMode, Satellite, Language, Taint }`. Tools, the guard and data access read the user from it. A tool running for owner A can only read and write owner A's data.

The owner of a session is resolved once, on its first turn, by `IIdentityResolver` from the evidence the client sends (`haUserId`, `deviceId`, `satelliteId`). It is stored with the evidence and never re-resolved for that session. In this version the resolver always returns the instance owner, with `resolved_by = owner_default`. The evidence is already recorded, so a later resolver can map voices or devices to different people without changing the data model.

Background work follows the same rule. A task runs headless as its owner. A webhook's events belong to the owner of the token that posted them. A CLI-created token belongs to the instance owner.

### Owner-scoped queries

All data access goes through EF Core global query filters on the current execution context's owner. Code running as A that lists sessions gets only A's sessions; another owner's artifact behaves as not found. Two extra guards back this up:

- Querying owner-scoped data **without** an execution context fails instead of returning unfiltered rows.
- Writing a row that belongs to another owner is refused.

`Secret` queries also return instance rows. Lookups from `/config` never read a user's secret rows. See [secrets](../guides/secrets.md).

### Sessions cannot be hijacked by id

Sessions from token clients are looked up by the token and the client's conversation id together, never by the conversation id alone. Two tokens that send the same `conversationId` get two separate sessions. A web user who sends another user's session id gets 404 and no run starts.

### Typed ids

Domain entities use strongly typed ids (`OwnerId`, `SessionId`, `RunId`, `PendingActionId`, `ApiTokenId` and others). Passing a `RunId` where a `SessionId` is expected does not compile, so a whole class of mix-ups cannot reach production.

### Per-owner identities in derived data

Memory chunk ids are a hash that includes the owner, so two owners writing the same title get different chunks. Artifacts live under `/data/artifacts/<owner>/` with a per-owner quota. Exports are per owner.

### Expected failures as results

Expected failures are returned as `Result` / `Result<T>` with a stable code and a message, and API endpoints map them to `ProblemDetails`; `session.not_found` becomes a 404 with that code. An unhandled exception becomes a 500 with code `server.unexpected`, a generic message and the request's trace id. The body never contains the exception message, type or stack trace, so internal paths and data do not leak through errors.

### Migrations

EF Core migrations run automatically at startup. Before any pending migration, Marshal copies the SQLite file next to itself with a timestamp, so a failed upgrade can be rolled back. See [upgrading](../installation/upgrading.md) and [database migrations](../development/database-migrations.md).

## Trade-offs

- **Cost now for later.** Every table carries an owner column and every query a filter, although one user is the norm today. The cost is small; the retrofit would not be.
- **Failing loudly without context.** Code paths that forget to set an execution context crash instead of quietly working. That is intended: a crash is visible, a leak is not.
- **One resolver for everyone in v1.** Everyone talking to a satellite is the instance owner. Do not share an instance between people who must not see each other's data until per-person resolution exists.

## Related

- [Privacy](privacy.md)
- [Security model](security-model.md)
- [Memory model](memory-model.md)
- [Data directory](../reference/data-directory.md)
