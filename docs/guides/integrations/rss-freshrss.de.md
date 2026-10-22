---
icon: fa-solid fa-rss
description: "Lass Marshal deine Nachrichten aus FreshRSS lesen, Artikel als gelesen markieren und den vollständigen Artikeltext für den täglichen Digest holen."
---

# FreshRSS verbinden

Lass Marshal deine Nachrichten aus FreshRSS lesen, Artikel als gelesen markieren und den vollständigen Artikeltext für den täglichen Digest holen.

## Bevor du anfängst

- Ein laufendes FreshRSS, das Marshal erreicht. Die API liegt unter `<url>/api/greader.php` (mit Google Reader kompatible API).
- Schalte in FreshRSS die API ein (Einstellungen, Authentifizierung, „API-Zugriff erlauben“) und setz unter Profil, API-Verwaltung ein **API-Passwort**. Das ist nicht dein Anmeldepasswort.
- Das API-Passwort als Secret gespeichert, zum Beispiel `/run/secrets/freshrss_api`.

## Schritte

::: steps
1. **Konfiguriere FreshRSS**

   In `/config/marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   freshrss:
     url: http://freshrss.local
     userName: lukas
     apiPasswordSecret: freshrss_api
     searchWindow: 500
     fullText: { maxBytes: 2097152, timeout: "00:00:15", maxCharacters: 20000 }
   ```

   `url`, `userName` und das auflösbare Secret sind zusammen Pflicht. Die RSS-Werkzeuge bleiben verborgen, bis FreshRSS konfiguriert ist. Die übrigen Schlüssel und ihre Bereiche stehen in der [Referenz von `freshrss`](../../configuration/marshal-yaml/integrations.md#freshrss).

2. **Starte Marshal neu**
:::

## Werkzeuge

Alle RSS-Werkzeuge sind safe. Artikelinhalte sind nicht vertrauenswürdig, ihr Lesen belastet den Durchlauf also mit der Quelle `rss`.

| Werkzeug | Was es tut |
|---|---|
| `rss.list_feeds` | Abonnierte Feeds mit ihren Kategorien. |
| `rss.list_categories` | Feed-Kategorien. |
| `rss.get_unread(category?, since?, limit)` | Ungelesene Artikel, die neuesten zuerst, mit einer kurzen Zusammenfassung. `limit` 1 bis 50. |
| `rss.get_article(id)` | Der Feed-Inhalt eines Artikels. |
| `rss.search(query, limit)` | Wörter in Titel oder Zusammenfassung über die neuesten `searchWindow` Artikel, gelesen und ungelesen. Die API von FreshRSS hat keine Suche, deshalb filtert Marshal lokal. |
| `rss.mark_read(ids)` | Markiert Artikel als gelesen, ohne Bestätigung. |
| `rss.fetch_full_text(articleId)` | Holt die Seite des Artikels und extrahiert lesbaren Text. |

Abonnieren, Abbestellen und Löschen gibt es nicht.

### Volltext nur über die Artikel-ID

`rss.fetch_full_text` nimmt eine Artikel-ID, nie eine URL. Marshal schlägt den Link des Artikels in FreshRSS nach und extrahiert den Text mit SmartReader. Ein URL-Argument scheitert an der Validierung, und keine Anfrage wird gestellt. So kann das Modell keine Seite holen, die ein Prompt gewählt hat. Das Lesen der Seite ist durch `fullText.maxBytes` und `fullText.timeout` begrenzt, der gelieferte Text durch `fullText.maxCharacters`.

## Der RSS-Digest

Der Digest wird nur als Dateien mitgeliefert, ohne eigenen Code: Der Skill `rss-digest` holt ungelesene Artikel seit dem letzten Digest, bewertet sie, wählt die besten aus, holt den Volltext, fasst zusammen, gruppiert und markiert die bearbeiteten Einträge als gelesen. Die Bewertung läuft über `agent.delegate` mit dem Template `rss-scorer` (Stufe `fast`, keine Werkzeuge) gegen die Interessen in deinem Gedächtnis. Nutze den Digest in einem [täglichen Briefing](../tasks/daily-briefing.md). Siehe [Skills](../skills.md) und [Agent-Templates](../agent-templates.md).

## Grenzen

- Die Seitenaufteilung folgt der `continuation` von FreshRSS bis zum angefragten Limit.
- `rss.search` sieht nur die neuesten `searchWindow` Artikel (Standard 500, höchstens 5000).
- Der Volltext hängt von der Website ab. Seiten hinter einer Bezahlschranke oder per Skript gerenderte Seiten liefern wenig Text.

## Prüfen, ob es funktioniert

1. Frag „welchen Feeds folge ich?“ Das Modell ruft `rss_list_feeds` auf.
2. Frag „was gibt es Neues in der Kategorie Tech?“ Du bekommst ungelesene Titel mit Zusammenfassungen.
3. Bitte „markier die als gelesen.“ FreshRSS zeigt sie als gelesen.

## Fehlerbehebung

- **Keine Werkzeuge `rss_`.** `freshrss.url` oder das Secret fehlt, oder das Secret löst sich nicht auf.
- **Die Authentifizierung scheitert.** Du hast das Anmeldepasswort genommen. Nimm das API-Passwort.
- **Leere Liste.** Der API-Zugriff von FreshRSS ist für den Benutzer aus.

## Verwandte Seiten

::: cards
- [Nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md)
- [Tägliches Briefing](../tasks/daily-briefing.md)
- [Secrets](../secrets.md)
- [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md#freshrss)
:::
