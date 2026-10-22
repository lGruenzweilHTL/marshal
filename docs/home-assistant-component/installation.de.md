---
icon: fa-solid fa-download
order: 10
description: "Installiere die Custom-Integration von Marshal, damit Home Assistant Assist-Unterhaltungen an Marshal schickt, und verbinde sie dann mit einem API-Token."
---

# Die Home-Assistant-Komponente installieren

Installiere die Custom-Integration von Marshal, damit Home Assistant Assist-Unterhaltungen an Marshal schickt, und verbinde sie dann mit einem API-Token.

## Bevor du anfängst

- Home Assistant 2026.9.0 oder neuer (die Version in `hacs.json`).
- Eine laufende Marshal-Instanz, die Home Assistant über HTTP(S) erreicht und die API-Version 1 anbietet. Siehe [Home Assistant verbinden](../getting-started/connect-home-assistant.md).
- Zugriff auf den Marshal-Host oder die Weboberfläche, um ein API-Token zu erstellen.

## Schritte

::: steps
1. **Erstelle ein API-Token**

   Erstelle ein Token nur mit dem Scope `conversation:write`:

   ```sh
   docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
   ```

   Oder öffne **Sicherheit** in der Weboberfläche von Marshal und erstelle es dort. Kopiere das Token; es wird einmal angezeigt. Siehe [Authentifizierung](../api/authentication.md).

2. **Kopiere die Integration in Home Assistant**

   Die Integration liegt in `ha/custom_components/marshal` des Marshal-Repositorys. Wähle einen Weg.

   **Von Hand.** Kopiere den Ordner `marshal` in den Ordner `custom_components` deiner Home-Assistant-Konfiguration, sodass `custom_components/marshal/manifest.json` existiert, und starte Home Assistant dann neu.

   **Benutzerdefiniertes Repository in HACS.** HACS installiert Integrationen aus einem Repository, das `custom_components/marshal/` in seiner Wurzel hat. Die Integration liegt derzeit in einem Unterordner des Marshal-Repositorys, HACS kann sie also erst installieren, wenn sie als eigenes Repository veröffentlicht ist. Ist es so weit, öffne HACS, wähle im Menü **Benutzerdefinierte Repositories**, füge die URL des Repositorys mit der Kategorie **Integration** hinzu, installiere **Marshal** und starte Home Assistant neu. Bis dahin nimm den Weg von Hand.

3. **Füg die Integration hinzu**

   1. Öffne **Einstellungen > Geräte & Dienste > Integration hinzufügen** und wähle **Marshal**.
   2. Gib die Adresse von Marshal und das Token ein. Siehe [Konfiguration](configuration.md).

4. **Nutze Marshal in einem Sprachassistenten**

   1. Öffne **Einstellungen > Sprachassistenten** und öffne einen Assistenten oder füge einen hinzu.
   2. Setz **Konversationsagent** auf **Marshal**.
   3. Schalte **Befehle bevorzugt lokal verarbeiten** aus. Ist es an, beantwortet Home Assistant Sätze, die es erkennt, und Marshal sieht sie nie.
   4. Wähle wie gewohnt Engines für Spracherkennung und Sprachausgabe.

   Zu Satelliten und Pipelines siehe [Assist-Pipeline](../guides/voice/assist-pipeline.md) und [Satelliten](../guides/voice/satellites.md).
:::

## Prüfen, ob es funktioniert

Öffne den Assist-Dialog in Home Assistant, wähle den Assistenten, der Marshal nutzt, und tippe eine Frage. Marshal antwortet. Unter der Integration erscheint ein Marshal-Gerät mit einer Konversationsentität.

## Verwandte Seiten

::: cards
- [Konfiguration](configuration.md)
- [Fehlerbehebung](troubleshooting.md)
:::
