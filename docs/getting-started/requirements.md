---
icon: fa-solid fa-clipboard-check
order: 20
---

# Requirements

What you need before you install Marshal.

## Host

| Item | Requirement |
|---|---|
| Operating system | Linux host with Docker Engine and the Compose plugin (`docker compose`) |
| Source | A clone of the Marshal repository. There is no published image; you build it |
| Build resources | The image build compiles .NET and the web app. Plan for 4 vCPUs and 8 GB RAM |
| Port | 8080 for Marshal (HTTP) |
| Storage | Space for `/data`. Artifacts default to a quota of 5120 MB per user (`artifacts.quotaMb`) |

## Language models

Marshal needs a chat model and an embedding model. You can run them locally or use a cloud model.

- Chat models: Ollama, an OpenAI-compatible server (llama.cpp `llama-server`, vLLM, LM Studio) or the Anthropic API. See [models setup](../installation/models-setup.md).
- Embedding model: required. Use a multilingual one such as `bge-m3`, otherwise a German question misses an English memory. Anthropic offers no embeddings.
- The example compose file runs Ollama with an NVIDIA GPU reservation. Remove the `deploy` block of the `ollama` service if you have no GPU.

## HTTPS

Browser sessions require HTTPS. Marshal rejects the first-run setup and every state-changing browser request over plain HTTP with the `auth.https_required` problem. Put a TLS reverse proxy in front of Marshal. See [reverse proxy](../installation/reverse-proxy.md) and [TLS and domains](../installation/tls-and-domains.md). Machine clients using API tokens, such as Home Assistant, can use plain HTTP on a trusted network.

## Home Assistant

- Home Assistant 2026.9.0 or newer (the minimum in `hacs.json`).
- A long-lived access token of an admin user if you want Marshal to read your home (`ha.tokenSecret`).
- For voice: an Assist pipeline with speech-to-text and text-to-speech.

## Related
- [Quick start](quick-start.md)
- [Supported versions](../reference/supported-versions.md)
