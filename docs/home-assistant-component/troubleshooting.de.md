---
icon: fa-solid fa-stethoscope
order: 50
description: "Lösungen für die häufigen Probleme beim Einrichten oder Nutzen der Marshal-Integration."
---

# Fehlerbehebung der Home-Assistant-Komponente

Lösungen für die häufigen Probleme beim Einrichten oder Nutzen der Marshal-Integration. Zu allgemeinen Problemen siehe [Fehlerbehebung](../guides/troubleshooting.md).

## Die Einrichtung scheitert

| Symptom | Ursache und Lösung |
|---|---|
| „Marshal ist unter dieser Adresse nicht erreichbar“ | Öffne die URL in einem Browser vom Home-Assistant-Host aus. Prüfe Port, Reverse Proxy und Schema. Home Assistant in einem Container muss den Hostnamen auflösen können. |
| „Marshal unterstützt die API-Version dieser Integration nicht“ | Marshal ist älter oder neuer als die Komponente. `curl <url>/api/versions` muss `1.0` listen. Aktualisiere Marshal oder die Komponente. |
| „Marshal hat das API-Token abgelehnt“ | Das Token ist falsch, widerrufen oder abgelaufen. Prüfe mit `curl -H "Authorization: Bearer <token>" <url>/api/v1/me`. Erstelle ein neues Token mit `conversation:write`. |
| „Gib eine vollständige http://- oder https://-Adresse ein“ | Ergänze das Schema, zum Beispiel `http://marshal.local:8080`. |
| Pfadpräfix | Läuft Marshal unter `http.pathBase`, nimm es in die URL auf. |

## Marshal antwortet nicht

- **Marshal sieht den Satz nie.** Schalte im Sprachassistenten **Befehle bevorzugt lokal verarbeiten** aus. Sonst beantwortet Home Assistant erkannte Sätze selbst.
- **Der Assistent nutzt einen anderen Agenten.** Prüfe, dass **Konversationsagent** auf **Marshal** steht.
- **Gesprochener Fehler „Marshal ist gerade nicht erreichbar“.** Marshal ist ausgefallen, der Netzweg ist unterbrochen, oder der Turn hat 75 Sekunden überschritten. Prüfe `<url>/health`.
- **Gesprochener Fehler „Marshal hat den Zugriff abgelehnt“.** Das Token wurde widerrufen oder hat kein `conversation:write`. Erstelle ein neues Token und füge die Integration neu hinzu.
- **Gesprochener Fehler „Marshal konnte das gerade nicht bearbeiten“.** Marshal hat einen Fehler geliefert. Prüfe das Log von Home Assistant auf die Warnung `Marshal turn failed` und das Log von Marshal oder den [Verlauf der Durchläufe](../guides/observability.md).

## Verhalten

- **Antwort in der falschen Sprache.** Die Komponente schickt die Sprache der Pipeline. Marshal unterstützt nur Deutsch und Englisch.
- **Eine lange Anfrage wird mit „ich arbeite daran“ beantwortet.** Der Turn hat die Latenzgrenze erreicht und läuft als Hintergrundaufgabe weiter. Erhöhe `agent.voiceLatencyCap` oder `agent.textLatencyCap`; siehe [Konfiguration von `agent`](../configuration/marshal-yaml/agent.md).
- **Der Satellit hört nach einer Frage nicht weiter zu.** Marshal setzt das pro Turn; prüfe, ob die letzte Antwort wirklich etwas gefragt hat und ob der Satellit fortgesetzte Unterhaltungen unterstützt.
- **Eine Antwort auf eine gesprochene Frage wird als neue Anfrage behandelt.** Die Rückfrage ist abgelaufen, kam von einem anderen Satelliten, oder `extra_system_prompt` hat die Integration nicht erreicht. Siehe [Rückfragen](../guides/voice/follow-ups.md).
- **Ein Werkzeug fehlt an einem Satelliten.** Das Satellitenprofil verweigert es womöglich. Siehe [satellites.yaml](../configuration/satellites-yaml.md).
- **Falscher Raum.** Der Bereich kommt vom Gerät oder der Satellitenentität in Home Assistant. Weise das Gerät einem Bereich zu.

## Logs sammeln

Schalte in der `configuration.yaml` von Home Assistant das Debug-Logging für die Integration ein:

```yaml [configuration.yaml]
logger:
  logs:
    custom_components.marshal: debug
```

## Verwandte Seiten

::: cards
- [Installation](installation.md)
- [Wie es funktioniert](how-it-works.md)
:::
