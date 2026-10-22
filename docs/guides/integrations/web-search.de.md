---
icon: fa-solid fa-magnifying-glass
description: "Lass Marshal Dinge im Web über deine eigene SearXNG-Instanz nachschlagen, ohne eine Ergebnisseite zu öffnen."
---

# Websuche verbinden (SearXNG)

Lass Marshal Dinge im Web über deine eigene SearXNG-Instanz nachschlagen, ohne eine Ergebnisseite zu öffnen.

## Bevor du anfängst

- Eine laufende [SearXNG](https://docs.searxng.org/)-Instanz, die Marshal erreicht.
- Die Instanz nur in deinem LAN oder in einem VPN. Aus dem Internet erreichbar ist SearXNG ein offener Such-Proxy für alle.
- Optional: ein Reverse Proxy vor SearXNG, der einen Header `Authorization` prüft. Speichere den Wert des Headers als Secret, zum Beispiel `/run/secrets/searxng_auth`.

## Schritte

::: steps
1. **Schalte die JSON-Ausgabe in SearXNG ein**

   Marshal nutzt die JSON-API von SearXNG. SearXNG liefert standardmäßig nur HTML. Erlaube JSON in der `settings.yml` von SearXNG:

   ```yaml [settings.yml]
   search:
     formats:
       - html
       - json
   ```

   Starte SearXNG neu. Eine Anfrage an `<url>/search?q=test&format=json` liefert jetzt JSON statt `403 Forbidden`.

2. **Konfiguriere Marshal**

   In `/config/marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   web:
     searxng:
       url: http://searxng.local:8080
       authHeaderSecret: searxng_auth      # optional
       engines: [google, bing, duckduckgo, wikipedia]
       maxResults: 8
   ```

   Nur `url` ist Pflicht. Ohne Abschnitt `web` bleiben die Webwerkzeuge verborgen. Die Schlüssel und ihre Bereiche stehen in der [Referenz von `web`](../../configuration/marshal-yaml/integrations.md#web-searxng-websuche).

3. **Wähle die Suchmaschinen**

   Marshal schickt die Liste `engines` mit jeder Anfrage, SearXNG fragt also nur diese Suchmaschinen. Beschränke die Liste auf große öffentliche Suchmaschinen, etwa die Standards `google`, `bing`, `duckduckgo` und `wikipedia`.

   Füg keine kleinen oder selbst gehosteten Suchmaschinen Dritter hinzu. Der Text der Anfrage erreicht jede Suchmaschine auf der Liste (siehe [Datenschutz](#datenschutz)). Eine Suchmaschine, die ein Fremder betreibt, würde deine Anfragen sehen, auch Anfragen in einem Durchlauf, der nicht vertrauenswürdige Inhalte gelesen hat.

4. **Starte Marshal neu**
:::

## Werkzeuge

Beide Webwerkzeuge sind safe, in belasteten und unbelasteten Durchläufen gleichermaßen.

| Werkzeug | Was es tut |
|---|---|
| `web.search(query, limit?)` | Eine Suche über SearXNG. Liefert pro Treffer Titel, Ausschnitt, URL, Domain und eine Ergebnis-ID. `query` höchstens 300 Zeichen, `limit` 1 bis `maxResults`. |
| `web.answer(question, answerType, values?)` | Sucht nach der Frage und gibt nur die Titel und Ausschnitte an den [abgeschotteten Extraktor](../../concepts/untrusted-content.md#extraktion-ohne-lesen). Liefert die Antwort (oder `unknown`) und die Domains der Ergebnisse. |

### Ergebnisse sind nicht vertrauenswürdig

Titel und Ausschnitte der Ergebnisse schreiben Fremde. Die Ausgabe von `web.search` wird als nicht vertrauenswürdige Daten eingepackt und belastet den Durchlauf mit der Quelle `web`.

`web.answer` lässt den Durchlauf bei typisierten Antworten (`boolean`, `number`, `integer`, `date`, `datetime`, `enum`) unbelastet. Eine Antwort `text` belastet den Durchlauf mit der Quelle `web`. Frag „Wann öffnet der Christkindlmarkt?“ als `date`, und eine Erinnerung, die das Modell danach anlegt, braucht keine Bestätigung.

`agent.extract` nimmt auch Verweise `web:<result id>` auf Ergebnisse der aktuellen Unterhaltung. Es liest nur Titel und Ausschnitt. Ergebnisse bleiben eine Stunde lang, danach scheitert ein Verweis mit `extract.not_found`.

### Keine Seite wird geöffnet

Marshal stellt genau eine HTTP-Anfrage pro Suche, an SearXNG. Es holt nie eine Ergebnis-URL, eingeschleuster Text kann Marshal also keine Seite besuchen lassen, die ein Angreifer gewählt hat.

## Datenschutz

Der Text der Anfrage geht über SearXNG an die konfigurierten Suchmaschinen, in jedem Durchlauf. Das schließt Durchläufe ein, die Mail, RSS oder Notizen gelesen haben. Suchanfragen sind nicht in der Bestätigungsstufe: Sie erreichen die Suchmaschinen auf deiner Liste, nicht einen Host, den ein Angreifer wählt, und eine Bestätigungsfrage würde den Text nicht privat halten.

Darf eine Frage dein Netz nicht verlassen, bitte Marshal nicht, danach zu suchen. Um die Websuche auszuschalten, entferne den Abschnitt `web`.

## Grenzen

- Ausschnitte sind kurz. `web.answer` liefert oft `unknown`; das Modell kann dann auf `web.search` zurückgreifen.
- Die Suchmaschinen dahinter begrenzen die Rate von SearXNG. Marshal meldet den Dienst dann als nicht verfügbar (`web.unavailable`) und versucht es nicht über seine Resilienzrichtlinie hinaus erneut.
- Marshal ordnet Ergebnisse nicht neu.

## Prüfen, ob es funktioniert

1. Bitte „Such im Web nach den Release Notes von Klipper.“ Das Modell ruft `web_search` auf und listet Ergebnisse mit ihren Domains.
2. Frag „Auf welches Datum fällt nächstes Jahr der Ostersonntag?“ Das Modell ruft `web_answer` mit `date` auf. Das Detail des Durchlaufs zeigt keine Belastung `web`.

## Fehlerbehebung

- **Keine Werkzeuge `web_`.** `web.searxng.url` fehlt.
- **Jede Suche scheitert mit `web.unavailable`.** Die JSON-Ausgabe ist in SearXNG nicht eingeschaltet (es antwortet mit `403`), die URL ist falsch, oder der Reverse Proxy lehnt den Header ab. Prüfe, dass sich `authHeaderSecret` auflöst.
- **Wenige oder keine Ergebnisse.** Die Suchmaschinen dahinter blockieren die SearXNG-Instanz. Prüfe die Logs und Statistiken der Suchmaschinen in SearXNG.

## Verwandte Seiten

::: cards
- [Nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md)
- [Datenschutz](../../concepts/privacy.md)
- [Secrets](../secrets.md)
- [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md#web-searxng-websuche)
:::
