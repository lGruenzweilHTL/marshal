---
icon: fa-solid fa-mobile-screen
description: "Füg Marshal dem Startbildschirm deines Handys hinzu, damit es wie eine App ohne Browserleisten öffnet."
---

# Die Weboberfläche als App installieren

Füg Marshal dem Startbildschirm deines Handys hinzu, damit es wie eine App ohne Browserleisten öffnet.

## Bevor du anfängst

- Marshal ist über HTTPS mit einem Zertifikat erreichbar, dem dein Handy vertraut ([TLS und Domains](../../installation/tls-and-domains.md)). Browser installieren Web-Apps und führen Service Worker nur auf sicheren Ursprüngen aus.
- Du kannst dich im Browser des Handys bei Marshal anmelden.

## Schritte

::: steps
1. **Öffne Marshal im Browser**

   Öffne deine Marshal-Adresse in Chrome (Android) oder Safari (iOS) und melde dich an.

2. **Füg es dem Startbildschirm hinzu**

   - **Android (Chrome):** Menü, **App installieren** oder **Zum Startbildschirm hinzufügen**.
   - **iOS (Safari):** Teilen, **Zum Home-Bildschirm**.

   Die App heißt Marshal und öffnet eigenständig.
:::

## Prüfen, ob es funktioniert

Starte die App vom Startbildschirm. Sie öffnet ohne Adressleiste und zeigt Start. Links aus Push-Benachrichtigungen öffnen im Browser und kommen angemeldet an, weil das Sitzungs-Cookie `SameSite=Lax` ist.

## Was die App zwischenspeichert

- Der Service Worker speichert nur die App-Hülle: Skripte, Styles, Schriften und Symbole.
- Er speichert nie API-Antworten oder den Stream der Live-Ereignisse.
- Ohne Verbindung zeigt die App eine statische Seite, „Marshal ist nicht erreichbar“, mit einer Schaltfläche zum erneuten Versuchen. Marshal behält keine Daten auf dem Gerät, offline ist also sonst nichts verfügbar.
- Eine neue Version von Marshal übernimmt die App beim nächsten Start. Du musst keine Caches leeren.

## Fehlerbehebung

- Keine Option zum Installieren: Die Seite wird nicht über HTTPS ausgeliefert, oder dem Zertifikat wird nicht vertraut.
- Die App zeigt eine alte Version: Schließ sie ganz und starte sie neu.
- Die Anmeldung scheitert über reines HTTP mit `auth.https_required`: Öffne Marshal über HTTPS.

## Verwandte Seiten

::: cards
- [Rundgang durch die Weboberfläche](tour.md)
- [Reverse Proxy](../../installation/reverse-proxy.md)
:::
