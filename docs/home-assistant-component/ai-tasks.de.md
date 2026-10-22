---
icon: fa-solid fa-robot
order: 40
description: "Nutze Marshal als AI-Task-Anbieter in Home Assistant (HA), damit eine Automation Marshal eine Frage stellen kann, optional mit Kamera-Snapshots, und die Antwort als Text oder strukturierte Daten nutzt."
---

# AI-Tasks aus Automationen ausführen

Nutze Marshal als AI-Task-Anbieter in Home Assistant (HA), damit eine Automation Marshal eine Frage stellen kann, optional mit Kamera-Snapshots, und die Antwort als Text oder strukturierte Daten nutzt.

## Bevor du anfängst

- Die Marshal-Komponente ist installiert und verbunden. Siehe [Installation](installation.md).
- Ein API-Token mit den Scopes `conversation:write` und `aitasks:write`. Ein Token, das vor den AI-Tasks erstellt wurde, hat nur `conversation:write`; Tokens behalten ihre Scopes, erstelle also ein neues.
- Für Aufgaben mit Bildern: ein Modell mit `vision: true` in der Stufe des Templates `ai_task` (standardmäßig `fast`). Siehe [Modelle](../configuration/marshal-yaml/models.md).

## Schritte

::: steps
1. **Erstelle ein Token mit beiden Scopes**

   ```sh
   docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write --scope aitasks:write
   ```

   Kopiere das Token. Es wird einmal angezeigt.

2. **Gib das Token in HA ein**

   Läuft die Komponente schon mit einem älteren Token, scheitert der erste AI-Task mit „Dem Marshal-API-Token fehlt der Scope aitasks:write“, und HA startet eine erneute Anmeldung: Öffne **Einstellungen > Geräte & Dienste**, wähle auf der Marshal-Karte, die nach Authentifizierung fragt, **Neu konfigurieren** und gib das neue Token ein. Der Flow prüft, dass das Token `aitasks:write` trägt; der Eintrag behält seine URL, und Unterhaltungen funktionieren die ganze Zeit weiter.

   Funktioniert das neue Token, widerrufe das alte mit `marshal tokens revoke <token-id>`.

3. **Nutze die Entität in einer Automation**

   Die Komponente fügt die Entität `ai_task.marshal` hinzu. Ruf die Aktion **AI Task: Daten generieren** auf:

   ```yaml
   alias: Describe the doorbell visitor
   triggers:
     - trigger: state
       entity_id: binary_sensor.doorbell
       to: "on"
   actions:
     - action: ai_task.generate_data
       data:
         entity_id: ai_task.marshal
         task_name: doorbell
         instructions: Who is at the door? Is there a parcel?
         structure:
           visitor:
             description: Who is at the door, in a few words
             required: true
             selector:
               text:
           parcel:
             description: Whether a parcel is visible
             required: true
             selector:
               boolean:
         attachments:
           - media_content_id: media-source://camera/camera.front_door
             media_content_type: image/jpeg
       response_variable: result
     - action: notify.mobile_app_phone
       data:
         message: "{{ result.data.visitor }}{{ ' with a parcel' if result.data.parcel else '' }}"
   ```

   Ohne `structure` ist `result.data` die Antwort als Text.
:::

## Was Marshal mit einer Aufgabe tut

- Jede Aufgabe ist ein Durchlauf in einer neuen Sitzung mit der Quelle `automation`, dem Auslöser `ai_task:<task_name>`, dem Template `ai_task` und der in HA konfigurierten Sprache. Die Ansicht Läufe listet ihn unter **Automation**; der Chat-Verlauf nicht.
- Das Gedächtnis funktioniert wie in einer Unterhaltung: Marshal kann nutzen, was es über dein Zuhause und dich weiß.
- Das eingebaute Template `ai_task` erlaubt keine Werkzeuge. Damit Aufgaben deinen Kalender oder Drucker lesen dürfen, kopiere `deploy/config.example/templates/ai_task.md` nach `/config/templates/ai_task.md` und bearbeite seine Werkzeugliste. Siehe [Agent-Templates](../configuration/templates-format.md#eingebaute-templates).
- Während einer Aufgabe kann niemand etwas bestätigen. Ein Werkzeugaufruf, der eine Bestätigung braucht, geht in die Freigabe-Warteschlange und auf dein Handy; die Aufgabe antwortet, dass die Aktion wartet. Sie läuft nur, wenn du sie später freigibst, und du bekommst eine Benachrichtigung mit niedriger Priorität und dem Ergebnis.
- Bilder sind nicht vertrauenswürdige Inhalte. Ein Durchlauf mit Bildern kann ohne deine Freigabe nicht in Gedächtnis oder Aufgaben schreiben.

> [!WARNING]
> Marshal behandelt `instructions` als deine eigenen Worte. Setz keinen Text von Dritten (einen To-do-Eintrag, den jemand anderes bearbeiten kann, eine Nachricht, eine Webseite) per Template in `instructions` ein. Übergib solche Inhalte als Anhang, oder halte die Werkzeugliste des Templates rein lesend.

## Kosten und Grenzen

- Jede Aufgabe ist ein Modellaufruf, und eine Aufgabe mit `structure` macht einen oder zwei mehr. Ein Auslöser, der oft feuert (jedes Bewegungsereignis), kostet entsprechend. Prüfe die Menge in der Ansicht Läufe mit dem Filter **Automation**.
- AI-Tasks zählen gegen `tasks.dailyCostCap`. Ist die Grenze ausgeschöpft, scheitern Aufgaben bis zum nächsten lokalen Tag mit „Das tägliche Kostenlimit von Marshal ist erreicht.“
- Eine Aufgabe läuft höchstens `aiTasks.timeoutSeconds` lang (standardmäßig 60 s). Lokale Vision-Modelle können langsam sein; nimm lieber ein schnelles Vision-Modell in der Stufe des Templates. Siehe [`aiTasks`](../configuration/marshal-yaml/ai-tasks.md).

## Fehlerbehebung

| Fehler in HA | Ursache |
|---|---|
| Dem Marshal-API-Token fehlt der Scope aitasks:write | Erstelle ein Token mit beiden Scopes und gib es im Schritt zur erneuten Anmeldung ein. |
| Marshal hat für diese Aufgabe kein Modell, das Bilder ansehen kann | Kein Modell mit `vision: true` in der Stufe des Templates. |
| Die Antwort von Marshal passte nicht zur gewünschten Struktur | Das Modell konnte zweimal hintereinander kein passendes JSON erzeugen. Vereinfache die Struktur oder nimm in `ai_task.md` eine stärkere Stufe. |
| Marshal hat die Aufgabe nicht rechtzeitig erledigt | Der Durchlauf hat länger als `aiTasks.timeoutSeconds` gedauert. |
| Marshal unterstützt keine KI-Aufgaben; aktualisiere Marshal | Marshal hat mit `404` geantwortet: Es ist älter als die AI-Tasks. |

## Verwandte Seiten

::: cards
- [API für AI-Tasks](../api/ai-tasks.md)
- [Die Komponente konfigurieren](configuration.md)
- [Freigabe-Warteschlange und Risikostufen](../concepts/risk-and-confirmation.md)
:::
