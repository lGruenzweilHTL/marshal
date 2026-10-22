---
icon: fa-solid fa-terminal
order: 60
description: "Funktionierende Beispiele mit curl und Python für die öffentlichen Endpunkte."
---

# API-Beispiele

Funktionierende Beispiele mit `curl` und Python für die öffentlichen Endpunkte. Ersetze `<token>` und den Host durch deine Werte.

Die Anfragezellen auf dieser Seite rufen eine Instanz aus deinem Browser auf: Wähle den Server, wähle **Authorize** und füge ein Token ein. Sie erreichen nur eine Instanz, die Anfragen von dieser Website erlaubt; siehe [Die API in der Doku ausprobieren](openapi.md#die-api-in-der-doku-ausprobieren).

## Version und Token prüfen

```sh
curl https://marshal.example.com/api/versions
curl -H "Authorization: Bearer <token>" https://marshal.example.com/api/v1/me
```

::: request v1 GetMe
:::

## Einen Turn einer Unterhaltung ausführen

`-N` schaltet die Ausgabepufferung von curl ab, damit du Ereignisse siehst, sobald sie ankommen.

::: code-group
```sh [curl]
curl -N -X POST https://marshal.example.com/api/v1/conversation/turns \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{"conversationId":"my-script-1","text":"What is on my calendar today?","language":"en"}'
```

```python [Python]
import json
import requests

BASE = "https://marshal.example.com"
HEADERS = {"Authorization": "Bearer <token>", "Accept": "text/event-stream"}

def turn(text: str, conversation_id: str):
    body = {"conversationId": conversation_id, "text": text, "language": "en"}
    with requests.post(f"{BASE}/api/v1/conversation/turns", json=body, headers=HEADERS, stream=True, timeout=75) as r:
        r.raise_for_status()
        event = None
        for line in r.iter_lines(decode_unicode=True):
            if line.startswith("event:"):
                event = line.removeprefix("event:").strip()
            elif line.startswith("data:"):
                data = json.loads(line.removeprefix("data:").strip())
                if event == "delta":
                    print(data["text"], end="", flush=True)
                elif event == "final":
                    print()
                    return data

final = turn("Turn off the kitchen light.", "my-script-1")
if final["continueConversation"]:
    final = turn(input("> "), "my-script-1")  # Marshal hat etwas gefragt
```
:::

::: request v1 CreateConversationTurn
body:
  conversationId: my-script-1
  text: What is on my calendar today?
  language: en
:::

## Einem Live-Strom folgen

::: code-group
```sh [curl]
curl -N -H "Authorization: Bearer <token>" "https://marshal.example.com/api/v1/events?topics=runs,tasks"
```

```python [Python]
import json
import requests

url = "https://marshal.example.com/api/v1/events"
with requests.get(url, params={"topics": "runs"}, headers={"Authorization": "Bearer <token>"}, stream=True) as r:
    event = None
    for line in r.iter_lines(decode_unicode=True):
        if line.startswith("event:"):
            event = line.removeprefix("event:").strip()
        elif line.startswith("data:") and event != "ping":
            print(event, json.loads(line.removeprefix("data:").strip())["payload"])
```
:::

## Einen Webhook senden

```sh
curl -X POST https://marshal.example.com/api/v1/hooks/proxmox \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"severity":"error","timestamp":1760000000,"fields":{"type":"vzdump","hostname":"pve1","job-id":"backup-nightly"}}'
```

## Fehler behandeln

```python
r = requests.get(f"{BASE}/api/v1/me", headers={"Authorization": "Bearer <token>"})
if r.status_code == 401:
    print("Token unknown, revoked or expired")
elif not r.ok:
    print(r.json()["code"], r.json()["detail"])
```

## Verwandte Seiten

::: cards
- [API-v1-Referenz](v1/index.md)
- [Unterhaltungsendpunkt](conversation.md)
- [Live-Ereignisse](live-events.md)
- [Webhooks](webhooks.md)
:::
