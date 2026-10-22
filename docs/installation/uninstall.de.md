---
icon: fa-solid fa-trash-can
order: 80
description: "Entferne Marshal und entscheide, was du behältst."
---

# Deinstallieren

Entferne Marshal und entscheide, was du behältst.

## Bevor du anfängst

- Exportiere, was du behalten willst: Gedächtnisexporte und Datenbankkopien in `/data/backups`, das Notizen-Repository in `/data/notes`, Artefakte in `/data/artifacts`.

## Schritte

::: steps
1. **Entferne die Home-Assistant-Integration**

   Lösche in Home Assistant den Integrationseintrag **Marshal**, entferne `custom_components/marshal` und starte neu. Stell in deinen Assist-Pipelines einen anderen Konversationsagenten ein.

2. **Stoppe Marshal**

   ```sh
   cd deploy
   docker compose down
   ```

   Die Volumes bleiben dabei erhalten.

3. **Lösche die Daten (optional)**

   > [!WARNING]
   > Das löscht Datenbank, Schlüssel für die Verschlüsselung, Backups, Artefakte und den Notizen-Klon endgültig.

   ```sh
   docker compose down --volumes
   ```

   Entferne `deploy/config` und alle `secrets`-Dateien, die du angelegt hast. Widerrufe vor dem Löschen bei den jeweiligen Diensten das langlebige Home-Assistant-Token, die API-Schlüssel und OAuth-Clients, die du Marshal gegeben hast, und die Webhook-Tokens (`marshal tokens revoke <id>`).

4. **Entferne die Images**

   ```sh
   docker image rm marshal:latest
   ```
:::

## Verwandte Seiten

::: cards
- [Backups und Wiederherstellung](../guides/backups-and-restore.md)
:::
