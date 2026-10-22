---
icon: fa-solid fa-play
order: 30
description: "Baue Marshal, starte es mit Ollama und HTTPS und melde dich an."
---

# Schnellstart

Baue Marshal, starte es mit Ollama und HTTPS und melde dich an. Am Ende hast du eine funktionierende Weboberfläche.

## Bevor du anfängst

- Docker mit dem Compose-Plugin, git und die [Voraussetzungen](requirements.md).
- Ein Hostname für Marshal, unten `<host>` genannt (zum Beispiel `marshal.lan`), den dein Browser auflösen kann.

## Schritte

::: steps
1. **Hol den Code und die Konfiguration**

   ```sh
   git clone https://github.com/lGruenzweilHTL/marshal.git
   cd marshal/deploy
   cp -r config.example config
   ```

2. **Bearbeite `config/marshal.yaml`**

   Setz deine Zeitzone und deinen Namen, und nimm ein kleines Modell für beide Stufen, damit der Download kurz bleibt:

   ```yaml
   instance:
     timeZone: Europe/Vienna
   owner:
     displayName: "Dein Name"
     language: de
   auth:
     providers:
       - name: local
         type: local
   models:
     fast:
       tier: fast
       provider: ollama
       model: "qwen3:8b"
       endpoint: "http://ollama:11434"
       contextLength: 32768
     standard:
       tier: standard
       provider: ollama
       model: "qwen3:8b"
       endpoint: "http://ollama:11434"
       contextLength: 32768
   embedding:
     provider: ollama
     model: "bge-m3"
     endpoint: "http://ollama:11434"
   ```

   Lass die anderen Abschnitte der Beispieldatei, wie sie sind. Marshal startet nicht, wenn eine Datei in `/config` ein Secret im Klartext enthält.

3. **Füge HTTPS hinzu**

   Leg `deploy/Caddyfile` an:

   ```text
   <host> {
   	tls internal
   	reverse_proxy marshal:8080 {
   		flush_interval -1
   	}
   }
   ```

   Leg `deploy/compose.caddy.yml` an:

   ```yaml [deploy/compose.caddy.yml]
   services:
     caddy:
       image: caddy:2
       restart: unless-stopped
       ports:
         - "443:443"
       volumes:
         - ./Caddyfile:/etc/caddy/Caddyfile:ro
         - caddy-data:/data
       depends_on:
         - marshal
   volumes:
     caddy-data:
   ```

   `tls internal` nutzt Caddys eigene Zertifizierungsstelle. Dein Browser warnt davor, bis du dieser Wurzel vertraust oder auf ein echtes Zertifikat wechselst ([TLS und Domains](../installation/tls-and-domains.md)).

4. **Bauen und starten**

   ```sh
   docker compose -f docker-compose.yml -f compose.caddy.yml up -d --build
   ```

5. **Lade die Modelle**

   ```sh
   docker compose exec ollama ollama pull qwen3:8b
   docker compose exec ollama ollama pull bge-m3
   ```

6. **Leg das erste Konto an**

   Öffne `https://<host>/setup`. Gib einen Anzeigenamen, einen Benutzernamen und ein Passwort mit mindestens 12 Zeichen ein. Dieses Konto wird Admin und übernimmt den Eigentümer der Instanz. Die Einrichtungsseite ist nicht mehr erreichbar, sobald der Eigentümer übernommen ist.
:::

## Prüfen, ob es funktioniert

```sh
docker compose logs marshal
curl -k https://<host>/health
```

`/health` liefert `{"status":"healthy",...}` oder `degraded` (beide mit HTTP 200). Öffne die Ansicht **Chat** in der Weboberfläche und schick eine Nachricht.

## Fehlerbehebung

- Der Container beendet sich: `docker compose logs marshal` listet jedes Konfigurationsproblem mit Datei und Schlüssel. Siehe [Fehlerbehebung](../guides/troubleshooting.md).
- Die Einrichtung meldet, dass Marshal über HTTPS geöffnet werden muss: Du hast `http://...:8080` geöffnet. Nimm die HTTPS-Adresse.

## Verwandte Seiten

::: cards
- [Home Assistant verbinden](connect-home-assistant.md)
- [Installation mit Docker Compose](../installation/docker-compose.md)
:::
