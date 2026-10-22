---
icon: fa-solid fa-clipboard-check
order: 20
description: "Was du brauchst, bevor du Marshal installierst."
---

# Voraussetzungen

Was du brauchst, bevor du Marshal installierst.

## Host

| Punkt | Voraussetzung |
|---|---|
| Betriebssystem | Linux-Host mit Docker Engine und dem Compose-Plugin (`docker compose`) |
| Quellcode | Ein Klon des Marshal-Repositorys. Es gibt kein veröffentlichtes Image; du baust es selbst |
| Ressourcen für den Build | Der Image-Build kompiliert .NET und die Web-App. Plane 4 vCPUs und 8 GB RAM ein |
| Port | 8080 für Marshal (HTTP) |
| Speicher | Platz für `/data`. Artefakte haben standardmäßig ein Kontingent von 5120 MB pro Benutzer (`artifacts.quotaMb`) |

## Sprachmodelle

Marshal braucht ein Chat-Modell und ein Embedding-Modell. Du kannst sie lokal betreiben oder ein Cloud-Modell nutzen.

- Chat-Modelle: Ollama, ein OpenAI-kompatibler Server (llama.cpp `llama-server`, vLLM, LM Studio) oder die Anthropic-API. Siehe [Modelle einrichten](../installation/models-setup.md).
- Embedding-Modell: Pflicht. Nimm ein mehrsprachiges wie `bge-m3`, sonst verfehlt eine deutsche Frage einen englischen Gedächtniseintrag. Anthropic bietet keine Embeddings an.
- Die Beispiel-Compose-Datei betreibt Ollama mit einer NVIDIA-GPU-Reservierung. Entferne den `deploy`-Block des Dienstes `ollama`, wenn du keine GPU hast.

## HTTPS

Browser-Sitzungen brauchen HTTPS. Marshal lehnt die Ersteinrichtung und jede zustandsändernde Browser-Anfrage über reines HTTP mit dem Problem `auth.https_required` ab. Stell einen TLS-Reverse-Proxy vor Marshal. Siehe [Reverse Proxy](../installation/reverse-proxy.md) und [TLS und Domains](../installation/tls-and-domains.md). Maschinen-Clients mit API-Tokens, etwa Home Assistant, können in einem vertrauenswürdigen Netz reines HTTP nutzen.

## Home Assistant

- Home Assistant 2026.9.0 oder neuer (das Minimum in `hacs.json`).
- Ein langlebiges Zugriffstoken eines Admin-Benutzers, wenn Marshal dein Zuhause lesen soll (`ha.tokenSecret`).
- Für Sprache: eine Assist-Pipeline mit Spracherkennung und Sprachausgabe.

## Verwandte Seiten

::: cards
- [Schnellstart](quick-start.md)
- [Unterstützte Versionen](../reference/supported-versions.md)
:::
