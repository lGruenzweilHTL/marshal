---
icon: fa-solid fa-database
description: "Marshal's rules for changing the EF Core model and adding migrations."
---

# Database migrations

Marshal's rules for changing the EF Core model. `dotnet-ef` is a local tool (`dotnet tool restore`).

```sh
dotnet ef migrations add <Name> --project src/Marshal.Data --output-dir Migrations
```

## Rules

- All entities and migrations live in `src/Marshal.Data`, also for entities another project owns (memory, tasks). Names are snake_case through `EFCore.NamingConventions`.
- Every user-owned row has `owner_id`, child tables included, and gets the owner-scoped query filter. Never read "the user" from a global.
- New owned data gets an ownership test with a second fake user (`TestDatabase` from `Marshal.Testing`).
- Migrations are additive and committed. Never edit a migration that has shipped. Marshal applies pending migrations at startup, after a backup of the SQLite file.

## Update in the same change

- The [data model](../diagrams/erd.md): the `erd-<area>-<part>.md` snippet that holds the entity, and the table of constraints ([maintaining the diagrams](../diagrams/README.md)).
- The [data directory reference](../reference/data-directory.md), when the change alters what lives in `/data`.
- The [upgrading guide](../installation/upgrading.md), when a migration needs attention from people who upgrade.

## Related

::: cards
- [Data ownership](../concepts/data-ownership.md)
- [Upgrading](../installation/upgrading.md)
:::
