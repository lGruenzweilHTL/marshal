---
icon: fa-solid fa-sliders
order: 20
description: "Die Integration wird über einen Config Flow mit zwei Feldern eingerichtet und hat einen Schritt zur erneuten Anmeldung mit einem neuen Token."
---

# Die Home-Assistant-Komponente konfigurieren

Die Integration wird über einen Config Flow mit zwei Feldern eingerichtet und hat einen Schritt zur erneuten Anmeldung mit einem neuen Token. Sie hat keine Optionen.

## Felder des Config Flows

| Feld | Beschreibung |
|---|---|
| URL | Adresse von Marshal, zum Beispiel `http://marshal.local:8080`. Muss `http://` oder `https://` sein. Nimm das Pfadpräfix mit auf, wenn Marshal unter einem läuft (`http.pathBase`). Ein Schrägstrich am Ende, Query und Fragment werden entfernt. |
| API-Token | Ein API-Token von Marshal mit den Scopes `conversation:write` (Assist) und `aitasks:write` (AI-Task-Entität). Ohne `aitasks:write` funktionieren Unterhaltungen, und AI-Tasks scheitern. |

Bevor der Flow den Eintrag anlegt, prüft er der Reihe nach:

1. `GET /api/versions` listet die API-Version `1.0`.
2. `GET /api/v1/me` nimmt das Token an.

| Angezeigter Fehler | Ursache |
|---|---|
| Gib eine vollständige http://- oder https://-Adresse ein | Die URL ist keine `http(s)`-URL mit Host. |
| Marshal ist unter dieser Adresse nicht erreichbar | Die Verbindung ist gescheitert oder hat zu lange gedauert (5 s Verbindungsaufbau, 10 s insgesamt für die Prüfungen). |
| Marshal unterstützt die API-Version dieser Integration nicht | `/api/versions` listet `1.0` nicht. |
| Marshal hat das API-Token abgelehnt | Marshal hat mit `401` oder `403` geantwortet. |
| Unerwartete Antwort von Marshal; siehe Protokoll | Jede andere Antwort, zum Beispiel eine Antwort, die kein JSON ist. |

Der Titel des Eintrags ist `Marshal (<host>)`. Die URL ist die eindeutige ID des Eintrags: Wer dieselbe Instanz zweimal hinzufügt, bricht mit „Diese Marshal-Instanz ist bereits eingerichtet“ ab.

## Optionen

Keine. Zeitlimits, Werkzeuge, Sprache und Verhalten der Satelliten konfigurierst du in Marshal: [`agent`](../configuration/marshal-yaml/agent.md) und [`satellites.yaml`](../configuration/satellites-yaml.md).

## URL oder Token ändern

Um die URL zu ändern, entferne den Eintrag und füge ihn neu hinzu.

Um das Token zu ändern, nutze den Schritt zur erneuten Anmeldung. HA startet ihn, wenn ein AI-Task von Marshal `401` oder `403` bekommt, und zeigt ihn unter **Einstellungen > Geräte & Dienste**. Der Schritt fragt nur nach dem Token, prüft es gegen `GET /api/v1/me` und verlangt den Scope `aitasks:write`:

| Angezeigter Fehler | Ursache |
|---|---|
| Marshal hat das API-Token abgelehnt | Marshal hat mit `401` oder `403` geantwortet. |
| Dem API-Token fehlt der Scope aitasks:write | Das Token ist gültig, trägt aber kein `aitasks:write`. |

Bei Erfolg behält der Eintrag seine URL und lädt mit dem neuen Token neu. Widerrufe danach das alte Token mit `marshal tokens revoke <token-id>`.

## Was die Integration anlegt

Ein Marshal-Gerät mit zwei Entitäten, die nach ihm benannt sind:

- eine Konversationsentität (`conversation.marshal`) für Assist, mit Deutsch und Englisch und gestreamten Antworten;
- eine AI-Task-Entität (`ai_task.marshal`), die Daten für Automationen erzeugt, mit Anhängen. Siehe [AI-Tasks](ai-tasks.md). Sie erzeugt keine Bilder.

## Verwandte Seiten

::: cards
- [Installation](installation.md)
- [Wie es funktioniert](how-it-works.md)
- [AI-Tasks aus Automationen ausführen](ai-tasks.md)
- [Maschinen-Tokens](../guides/authentication/machine-tokens.md)
:::
