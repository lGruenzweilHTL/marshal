---
icon: fa-solid fa-house-signal
order: 40
description: "Installiere die Marshal-Integration, gib ihr ein API-Token und wähle Marshal als Konversationsagenten einer Assist-Pipeline."
---

# Home Assistant verbinden

Installiere die Marshal-Integration, gib ihr ein API-Token und wähle Marshal als Konversationsagenten einer Assist-Pipeline.

## Bevor du anfängst

- Ein laufendes Marshal ([Schnellstart](quick-start.md)), das Home Assistant über HTTP oder HTTPS erreicht.
- Home Assistant 2026.9.0 oder neuer.

## Schritte

::: steps
1. **Installiere die Integration**

   Kopiere `ha/custom_components/marshal` aus dem Marshal-Repository in den Ordner `custom_components` deiner Home-Assistant-Konfiguration, sodass `custom_components/marshal/manifest.json` existiert. Starte Home Assistant neu. Zu HACS und Details siehe [die Komponente installieren](../home-assistant-component/installation.md).

2. **Erstelle ein API-Token**

   Öffne in der Weboberfläche **Sicherheit** und erstelle ein Token mit dem Scope `conversation:write`. Oder führe aus:

   ```sh
   docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
   ```

   Das Token wird einmal angezeigt. Kopiere es.

3. **Füge die Integration hinzu**

   Öffne in Home Assistant **Einstellungen, Geräte & Dienste, Integration hinzufügen, Marshal**. Gib die Basis-URL von Marshal ein (zum Beispiel `http://marshal.lan:8080`, mit Pfad-Basis, falls du eine nutzt) und das Token. Die Einrichtung prüft, ob Marshal erreichbar ist, API-Version 1 unterstützt und das Token annimmt.

4. **Wähle Marshal in einer Pipeline aus**

   1. Öffne **Einstellungen, Sprachassistenten** und wähle deinen Assistenten.
   2. Setz **Konversationsagent** auf **Marshal**.
   3. Schalte **Befehle bevorzugt lokal verarbeiten** aus. Sonst beantwortet Home Assistant erkannte Sätze selbst, und Marshal sieht sie nie.

5. **Lass Marshal dein Zuhause lesen (optional)**

   Erstelle in Home Assistant ein langlebiges Zugriffstoken eines Admin-Benutzers, speichere es als Secret `ha_token` (siehe [Secrets](../guides/secrets.md)) und ergänze `marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   ha:
     url: http://homeassistant.local:8123
     tokenSecret: ha_token
   ```

   Starte Marshal neu.
:::

## Prüfen, ob es funktioniert

Öffne den Assist-Dialog in Home Assistant und tippe eine Frage. Die Antwort von Marshal erscheint. In der Weboberfläche taucht ein Durchlauf unter **Durchläufe** auf.

## Fehlerbehebung

Siehe [Fehlerbehebung der Komponente](../home-assistant-component/troubleshooting.md).

## Verwandte Seiten

::: cards
- [Anleitung zur Home-Assistant-Integration](../guides/integrations/home-assistant.md)
- [Satelliten](../guides/voice/satellites.md)
:::
