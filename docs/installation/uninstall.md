---
icon: fa-solid fa-trash-can
order: 80
description: "Remove Marshal and decide what to keep."
---

# Uninstall

Remove Marshal and decide what to keep.

## Before you start

- Export what you want to keep: memory exports and the database copies in `/data/backups`, the notes repository in `/data/notes`, artifacts in `/data/artifacts`.

## Steps

::: steps
1. **Remove the Home Assistant integration**

   In Home Assistant delete the **Marshal** integration entry, remove `custom_components/marshal`, and restart. Set another conversation agent in your Assist pipelines.

2. **Stop Marshal**

   ```sh
   cd deploy
   docker compose down
   ```

   This keeps the volumes.

3. **Delete data (optional)**

   > [!WARNING]
   > This deletes the database, encryption keys, backups, artifacts and notes clone permanently.

   ```sh
   docker compose down --volumes
   ```

   Remove `deploy/config` and any `secrets` files you created. Revoke the Home Assistant long-lived token, API keys and OAuth clients you gave Marshal at their services, and the webhook tokens (`marshal tokens revoke <id>`) before deleting.

4. **Remove images**

   ```sh
   docker image rm marshal:latest
   ```
:::

## Related

::: cards
- [Backups and restore](../guides/backups-and-restore.md)
:::
