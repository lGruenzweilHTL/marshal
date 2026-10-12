# API examples

Working `curl` and Python examples for the public endpoints. Replace `<token>` and the host with your values.

## Check the version and the token

```sh
curl https://marshal.example.com/api/versions
curl -H "Authorization: Bearer <token>" https://marshal.example.com/api/v1/me
```

## Run a conversation turn

`-N` turns off curl's output buffering so you see events as they arrive.

```sh
curl -N -X POST https://marshal.example.com/api/v1/conversation/turns \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{"conversationId":"my-script-1","text":"What is on my calendar today?","language":"en"}'
```

```python
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
    final = turn(input("> "), "my-script-1")  # Marshal asked something
```

## Follow a live stream

```sh
curl -N -H "Authorization: Bearer <token>" "https://marshal.example.com/api/v1/events?topics=runs,tasks"
```

```python
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

## Post a webhook

```sh
curl -X POST https://marshal.example.com/api/v1/hooks/proxmox \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"severity":"error","timestamp":1760000000,"fields":{"type":"vzdump","hostname":"pve1","job-id":"backup-nightly"}}'
```

## Handle errors

```python
r = requests.get(f"{BASE}/api/v1/me", headers={"Authorization": "Bearer <token>"})
if r.status_code == 401:
    print("Token unknown, revoked or expired")
elif not r.ok:
    print(r.json()["code"], r.json()["detail"])
```

## Related

- [Conversation endpoint](conversation.md)
- [Live events](live-events.md)
- [Webhooks](webhooks.md)
