---
icon: fa-solid fa-heart-pulse
description: "Schick Benachrichtigungen von Uptime Kuma und Proxmox VE an Marshal, damit Beobachtungen reagieren können, wenn ein Monitor ausfällt, ein Zertifikat bald abläuft oder ein Backup fehlschlägt."
---

# Webhooks von Uptime Kuma und Proxmox empfangen

Schick Benachrichtigungen von Uptime Kuma und Proxmox VE an Marshal, damit Beobachtungen reagieren können, wenn ein Monitor ausfällt, ein Zertifikat bald abläuft oder ein Backup fehlschlägt.

## Bevor du anfängst

- Marshal ist von Uptime Kuma oder Proxmox unter einer HTTPS-Adresse erreichbar, zum Beispiel `https://<marshal>`. Siehe [Reverse Proxy](../../installation/reverse-proxy.md).
- Shell-Zugriff auf den Marshal-Container, um ein Token anzulegen.
- Uptime Kuma, oder Proxmox VE 8.3 oder neuer mit dem Benachrichtigungssystem.

## Wie der Webhook-Empfang funktioniert

Marshal nimmt Webhooks unter `POST /api/v1/hooks/{source}` an. Jede Quelle hat ihren eigenen Token-Scope `hooks:<source>`, und der Eigentümer des Ereignisses ist der Eigentümer des Tokens. Marshal macht aus einem Webhook ein Ereignis auf dem Event-Bus und ruft kein Modell auf. Eine Beobachtung löst dann auf das Ereignis aus. Siehe [Ereignis-Auslöser](../tasks/event-triggers.md).

| Antwort | Bedeutung |
|---|---|
| 202 | Angenommen. Kommt auch, wenn kein Ereignis veröffentlicht wurde (eine Wiederholung im Entprellfenster, eine Testbenachrichtigung, ein ignorierter Typ). |
| 400 | Der Body ließ sich nicht lesen. Nichts wurde veröffentlicht. |
| 401 | Token fehlt, ist widerrufen oder abgelaufen. |
| 403 | Dem Token fehlt der Scope `hooks:<source>`. |
| 404 | Keine solche Quelle. |
| 413 | Body größer als 64 KB. |
| 429 | Das Token hat mehr als `webhooks.requestsPerMinute` Anfragen in einer Minute gesendet. |

Marshal verwirft ein Ereignis, dessen Quelle, Typ und Entität einem Ereignis gleichen, das es innerhalb von `webhooks.debounceSeconds` für denselben Eigentümer veröffentlicht hat. Ein Zustandswechsel, etwa `down` gefolgt von `up`, ist keine Wiederholung.

```yaml
webhooks:
  debounceSeconds: 60      # Standard 60
  requestsPerMinute: 60    # pro Token, Standard 60
```

Die Ereignisse dieser Quellen sind vertrauenswürdig. Ihre Nutzdaten enthalten nur IDs, Namen und Zahlen, nie freien Text des Absenders.

## Uptime Kuma

::: steps
1. **Erstelle ein Token**

   ```sh
   docker exec marshal marshal tokens create --name kuma --scope hooks:uptime-kuma
   ```

   Marshal gibt das Token einmal aus. Kopiere es. Siehe [Maschinen-Tokens](../authentication/machine-tokens.md).

2. **Füg in Uptime Kuma eine Webhook-Benachrichtigung hinzu**

   Öffne **Settings > Notifications > Setup Notification** und wähle **Webhook**:

   | Feld | Wert |
   |---|---|
   | Post URL | `https://<marshal>/api/v1/hooks/uptime-kuma` |
   | Request Body | `application/json` (Formulardaten gehen auch) |
   | Additional Headers | `{"Authorization": "Bearer <token>"}` |

   Schalte die Benachrichtigung für die Monitore ein, die du willst, oder für alle Monitore.

3. **Ereignisse**

   | Ereignistyp | Wann | Entität | Nutzdaten |
   |---|---|---|---|
   | `monitor.down` | Heartbeat-Status 0 | Name des Monitors | Monitor-ID, Name, Typ, URL, Heartbeat-Zeit, Ping in ms, `important` |
   | `monitor.up` | Heartbeat-Status 1 | Name des Monitors | wie oben |
   | `monitor.pending` | Heartbeat-Status 2 | Name des Monitors | wie oben |
   | `monitor.maintenance` | Heartbeat-Status 3 | Name des Monitors | wie oben |
   | `certificate.expiring` | Zertifikatshinweis ohne Heartbeat | Name des Monitors | Name des Monitors, verbleibende Tage |

   Die Testnachricht von Uptime Kuma und unbekannte Hinweise veröffentlichen nichts. Die Nutzdaten enthalten nie den Text `msg` von Uptime Kuma.

4. **Leg eine Beobachtung an**

   Bitte Marshal: „Sag mir, wenn das NAS ausfällt.“ Marshal legt eine Beobachtung mit `source: uptime-kuma`, `type: monitor.down`, `entity: NAS` an.
:::

## Webhooks von Proxmox VE

::: steps
1. **Erstelle ein Token**

   ```sh
   docker exec marshal marshal tokens create --name proxmox --scope hooks:proxmox
   ```

2. **Füg in Proxmox ein Webhook-Ziel hinzu**

   Öffne **Datacenter > Notifications > Add > Webhook** und setz:

   | Feld | Wert |
   |---|---|
   | Methode und URL | `POST https://<marshal>/api/v1/hooks/proxmox` |
   | Header | `Authorization` = `Bearer {{ secrets.token }}` |
   | Header | `Content-Type` = `application/json` |
   | Body | `{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}` |
   | Secret | `token` = das Token aus Schritt 1 |

   Füg dann einen Notification Matcher hinzu, der an dieses Ziel sendet. Ohne Matcher sendet Proxmox nichts.

3. **Ereignisse**

   | Ereignistyp | Benachrichtigung in Proxmox | Entität |
   |---|---|---|
   | `backup.failed` | `vzdump` mit Schweregrad `error` | Job-ID, sonst Hostname |
   | `backup.ok` | `vzdump` mit jedem anderen Schweregrad | Job-ID, sonst Hostname |
   | `replication.failed` | `replication` | Job-ID, sonst Hostname |
   | `node.fenced` | `fencing` | Job-ID, sonst Hostname |
   | `updates.available` | `package-updates` | Job-ID, sonst Hostname |

   Die Nutzdaten enthalten `type`, `severity`, `hostname`, `jobId` und die Zeit der Benachrichtigung. Andere Typen wie `system-mail` und Bodies ohne `fields.type` veröffentlichen nichts. Ein Body, der kein JSON ist oder dessen `fields` kein Objekt ist, bekommt 400.

   Um den Cluster bei Bedarf zu lesen, siehe [Proxmox](proxmox.md).
:::

## Prüfen, ob es funktioniert

1. Nutze die Schaltfläche **Test** in Uptime Kuma oder Proxmox. Marshal antwortet mit 202 und veröffentlicht kein Ereignis. Ein 401 heißt, das Token ist falsch, ein 403 heißt, der Scope passt nicht zur Quelle.
2. Pausiere einen Monitor oder führe ein Backup aus, das fehlschlägt, und öffne dann die Weboberfläche. Eine passende Beobachtung macht ihre Aufgabe fällig.
3. Liste die Tokens auf, um zu sehen, dass das Token existiert:

   ```sh
   docker exec marshal marshal tokens list
   ```

## Verwandte Seiten

::: cards
- [Ereignis-Auslöser](../tasks/event-triggers.md)
- [Maschinen-Tokens](../authentication/machine-tokens.md)
- [Webhook-API](../../api/webhooks.md)
- [`webhooks` in marshal.yaml](../../configuration/marshal-yaml/webhooks.md)
- [Referenz der Ereignisse](../../reference/events.md)
- [Medienanfragen](media.md)
  Webhooks von Seerr
:::
