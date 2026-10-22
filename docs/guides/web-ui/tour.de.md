---
icon: fa-solid fa-route
description: "Finde dich in der Web-App von Marshal zurecht: was jedes Ziel zeigt und was du dort tun kannst."
---

# Rundgang durch die Weboberfläche

Finde dich in der Web-App von Marshal zurecht: was jedes Ziel zeigt und was du dort tun kannst.

![Marshal Start auf dem Desktop: die gruppierte Seitennavigation, zwei offene Freigaben mit Schaltflächen zum Freigeben und Ablehnen, eine Druckerkarte und laufende Aufgaben](../../images/dashboard.png)

## Bevor du anfängst

- Öffne Marshal über HTTPS (über deinen [Reverse Proxy](../../installation/reverse-proxy.md)). Browser-Sitzungen funktionieren nicht über reines HTTP.
- Melde dich mit einem [lokalen Konto](../authentication/local-accounts.md) oder über [OIDC](../authentication/oidc.md) an.

## Aufbau

Auf dem Desktop listet eine gruppierte Seitennavigation jedes Ziel. Auf einem Handy zeigt eine Leiste unten Start, Chat, Eingang, Drucker und Mehr. Mehr öffnet den Rest. Ziele, die dich brauchen (offene Freigaben, offene Vorschläge, ungelesene kritische Benachrichtigungen), zeigen ein Zählabzeichen. Im [Demo-Modus](../demo-mode.md) steht auf dem Desktop ein Abzeichen „Demo“ neben dem Logo und auf dem Handy über der Seite.

![Marshal Eingang auf einem Handy: zwei offene Freigaben mit Schaltflächen zum Freigeben und Ablehnen und eine Tab-Leiste unten](../../images/mobile.png)

Jede Ansicht funktioniert in Handybreite. Die Ansichten, in denen du handelst (Start, Eingang, Chat, Drucker), sind zuerst fürs Handy gestaltet. Die Ansichten, in denen du verwaltest (Gedächtnis, Läufe, Kosten, Konfiguration, Mailkonten, Sicherheit), sind zuerst für den Desktop gestaltet und können auf dem Handy einen reduzierten Aufbau zeigen.

## Ziele

| Gruppe | Ziel | Was es tut |
|---|---|---|
| Handeln | Start | Was dich zuerst braucht: offene Freigaben mit Freigeben und Ablehnen, Druckerwarnungen, laufende Aufgaben und nächste Termine, letzte Benachrichtigungen. Der Not-Aus erscheint, solange Hintergrundaufgaben laufen. |
| Handeln | Chat | Text-Chat mit Marshal. Antworten werden gestreamt. Markdown, Tabellen und Code werden dargestellt; Bilder erscheinen nur vom eigenen Ursprung von Marshal. Artefakte erscheinen inline (Bilder) oder als Karten, die den Viewer öffnen. Drucker- und Musik-Widgets und MCP Apps erscheinen unter der Antwort. Frühere Unterhaltungen, getippte und gesprochene, stehen neben dem Chat. |
| Handeln | Eingang | Der eine Ort für alles, was auf dich wartet: Freigaben (eingefrorene Aktion, Gründe, verbleibende Zeit), Eingabeanfragen von MCP-Servern (ein Formular oder ein Link mit „Fertig“), Code-Vorschläge (Diff, mergen oder ablehnen) und der Verlauf der Benachrichtigungen, filterbar nach Ergebnis. |
| Handeln | Drucker | Live-Zustand der Drucker. |
| Aktivität | Aufgaben | Aufgaben und Beobachtungen mit Status, Auslöser, nächstem Termin, Budget und letztem Ergebnis. Ziel bearbeiten, Aufgabe abbrechen, Not-Aus nutzen. |
| Aktivität | Läufe | Was Marshal getan hat, Schritt für Schritt, und warum, mit dem Reasoning jedes Modellaufrufs (eingeklappt), den Werkzeugen, die jeder Durchlauf geladen hat, und wie lange Sprach-Turns bis zu den ersten Worten gebraucht haben. Siehe [Ansicht Läufe](#ansicht-läufe). |
| Aktivität | Artefakte | Dateien, die Marshal erzeugt oder bekommen hat. Siehe [Artefakte](../artifacts.md). |
| Aktivität | Kosten | Modellnutzung und Kosten. |
| Gedächtnis | Einträge, Projekte, Karte, Prüfung | Alles sehen und bearbeiten, was Marshal sich merkt. Siehe [Gedächtnis](../memory.md). |
| Verbindungen | Mailkonten | Mailkonten verbinden. Siehe [Mail](../integrations/mail.md). |
| Einstellungen | Präferenzen | Sprache, Zeitzone, Darstellung, Ruhezeiten, Benachrichtigungsziele, Nicht stören. |
| Einstellungen | Sicherheit | API-Tokens, Passkeys und Secrets. |
| Einstellungen | System | Zustand und Not-Aus. Im Demo-Modus auch die Liste der simulierten Integrationen. |
| Einstellungen | Konfiguration | Nur lesbare Ansicht der geladenen Konfiguration und Freigabe geänderter MCP-Werkzeuge. Nur für Admins. |

![Gedächtnisansicht von Marshal mit gemerkten Einträgen](../../images/memory.png)

## Ansicht Läufe

Die Liste der Läufe zeigt jeden Durchlauf mit Quelle, Auslöser, Ergebnis, Kosten und „ersten Worten“: der Zeit von der Anfrage bis zum ersten gesprochenen oder gestreamten Text. Wähle „Langsamste erste Worte“, um nach dieser Zeit zu sortieren, die langsamsten zuerst, und finde die Sprach-Turns, die sich langsam angefühlt haben. Durchläufe ohne gestreamten Text fallen aus dieser Reihenfolge heraus.

Das Detail eines Durchlaufs listet seine Schritte. Der Kontextschritt zeigt „Geladene Werkzeuge“: die Werkzeug-Namensräume, die der Durchlauf dem Modell angeboten hat, und warum jeder geladen wurde (`core`, `session`, `hint`, `retrieval` mit seinem Ähnlichkeitswert, `find_tools` oder `call`). Ladevorgänge während des Durchlaufs erscheinen am Werkzeugschritt, der sie ausgelöst hat. Ein Namensraum, der hätte geladen werden sollen, es aber nicht wurde, weist auf eine Beschreibung oder eine Schwelle hin, die du abstimmen solltest; siehe [Werkzeugsuche](../../concepts/tool-visibility.md#werkzeugsuche).

Ein Sprachdurchlauf bekommt außerdem eine Zeitleiste über den Schritten: Kontextaufbau, Modellaufrufe (mit einer Markierung beim ersten Token) und Werkzeugaufrufe, aneinandergereiht von der Anfrage bis zum Ende des Durchlaufs, mit markierten ersten Worten.

## Freigaben aus einer Push-Benachrichtigung

Ein Tipp auf eine Push-Benachrichtigung zu einer Freigabe öffnet eine fokussierte Freigabeansicht. Sie zeigt die eingefrorene Aktion, ihre Argumente und die verbleibende Zeit, mit Freigeben und Ablehnen ohne Scrollen erreichbar. Freigeben, Ablehnen und Mergen sind protokollierte Benutzeraktionen. Siehe [Freigaben](../tasks/approvals.md).

## Chat-Verlauf

Der Chat behält deine Unterhaltungen. Auf dem Desktop steht die Liste früherer Unterhaltungen neben dem Chat, die neuesten zuerst. Auf dem Handy öffnet die Verlaufs-Schaltfläche im Kopf des Chats die Liste. Jede Unterhaltung hat ihre eigene Adresse (`/chat/{id}`), ein Neuladen oder ein Lesezeichen bringt sie also zurück.

- **Web-Unterhaltungen** gehen dort weiter, wo du aufgehört hast, egal wie viel Zeit vergangen ist. Nach ein paar Minuten Leerlauf ruht die Unterhaltung: Eine ausstehende Bestätigung fällt weg, und Marshal fasst die neuen Turns ins Gedächtnis zusammen, aber die nächste Nachricht setzt dieselbe Unterhaltung fort.
- **Sprachunterhaltungen** von Home-Assistant-Satelliten erscheinen mit einem Mikrofon und dem Satelliten oder Bereich. Jeder gesprochene Turn ist markiert. Im Web-Chat sind sie nur lesbar: Setz sie per Stimme fort oder beginne einen neuen Chat.
- **API-Unterhaltungen** sind mit „API“ markiert und ebenfalls nur lesbar.

## Reasoning und Werkzeugschritte im Chat

Während Marshal an einer Antwort arbeitet, zeigt der Chat, was es tut:

- **Reasoning.** Hat das Modell `reasoning` gesetzt (siehe [Modelle](../../configuration/marshal-yaml/models.md)), streamt sein Reasoning in einen Block über der Antwort. Der Block ist offen, solange das Modell nachdenkt, und klappt zu, sobald der Antworttext beginnt. Wähle „Reasoning“, um ihn zu öffnen oder zu schließen. Der Text wird so gezeigt, wie er geschrieben ist, nie als Markdown, Links und Bilder darin bleiben also wirkungslos.
- **Werkzeugschritte.** Jeder Werkzeugaufruf ist eine Zeile: der Name des Werkzeugs, eine kurze Zusammenfassung seiner Argumente, sein Zustand (läuft, fertig, fehlgeschlagen, nicht erlaubt, wartet auf Bestätigung), wie lange er gedauert hat und ein kurzes Ergebnis. Ergebnisse sensibler Werkzeuge, etwa Mail und Notizen, werden nie zusammengefasst. Fortschrittsmeldungen des Turns erscheinen in derselben Liste.

„Denkt nach…“ erscheint nur, bis das erste davon ankommt. Eine wieder geöffnete Unterhaltung zeigt beides eingeklappt. Reasoning, das auf private Inhalte folgte (eine Mail oder eine Notiz), wird nicht behalten; der Chat zeigt dann stattdessen einen Hinweis. Sprach-Turns fordern nie Reasoning an, und Home Assistant und API-Clients bekommen nie Reasoning oder Werkzeugschritte.

## Widgets und Apps im Chat

Manche Werkzeugaufrufe zeigen eine interaktive Ansicht unter der Antwort. Sprach- und API-Turns nie.

- **Drucker-Widget.** Nach `printer.status` der Live-Zustand des Druckers: Fortschritt, Schicht, Restzeit und Temperaturen, mit Pause, Fortsetzen und Abbrechen. Abbrechen und Fortsetzen sind in der Bestätigungsstufe; das Widget fragt vorher in einem Dialog, und die Bestätigung dort zählt als deine Bestätigung.
- **Musik-Widget.** Nach `music.queue` oder `music.play`, was auf dem Lautsprecher läuft und was als Nächstes kommt, mit Abspielen, Pause, Zurück, Weiter und Lautstärke. Es aktualisiert sich alle 5 Sekunden.
- **MCP Apps.** Ein Server mit [`apps: true`](../../configuration/mcp-servers.md#mcp-apps) kann seine eigene Oberfläche in einem Sandbox-Frame zeigen, beschriftet mit dem Namen des Servers.

Jede Schaltfläche eines Widgets ist ein aufgezeichneter Durchlauf mit dem Auslöser `widget:printer` oder `widget:music`. Eine wieder geöffnete Unterhaltung zeigt Widgets mit dem aktuellen Zustand. Eine App erscheint nur wieder, solange ihr Server sie noch anbietet; sonst sagt eine Karte, dass die Ansicht nicht mehr verfügbar ist.

## Ergebnisse aus dem Hintergrund im Chat

Dauert ein Turn zu lange, oder verschiebt Marshal ihn selbst in den Hintergrund, sagt die Antwort, dass Marshal daran arbeitet. Ist die Hintergrundaufgabe fertig, erscheint ihr Ergebnis in derselben Unterhaltung als Antwort mit der Markierung „Ergebnis aus dem Hintergrund“, ohne Neuladen. Der Eingang bekommt das Ergebnis trotzdem auch. Ist die Verbindung in der Zwischenzeit abgerissen, lädt der Chat die Unterhaltung neu, sobald er sich wieder verbindet. Im nächsten Turn kennt Marshal das Ergebnis, du kannst also Rückfragen dazu stellen.

## Bestätigungen im Chat

Endet ein Turn mit einer ausstehenden Aktion, zeigt der Chat die Frage mit ihrem Grund. Tippe „bestätigt“, um freizugeben.

## Konfigurationsansicht

Admins sehen die geladenen Skills, Templates, MCP-Server (Status und Zahl der Werkzeuge), Risikoregeln, Modelle ohne Secrets und Satellitenprofile, dazu den letzten Fehler beim Neuladen pro Datei. Ist ein Neuladen von `risk.yaml` gescheitert, zeigt die Ansicht den Fehler und dass die vorherigen Regeln aktiv bleiben. Die Ansicht bearbeitet keine Dateien.

MCP-Werkzeuge, die seit der ersten Erkennung ihres Servers neu sind oder sich geändert haben, warten hier auf Freigabe, mit der vorherigen und der aktuellen Definition nebeneinander. Gib jedes frei oder lehne es ab; die Entscheidung gilt ab dem nächsten Turn. Siehe [Werkzeug-Pinning](../../configuration/mcp-servers.md#werkzeug-pinning).

## Banner zum Zustand

Solange Marshal [Warnungen zum Zustand](../../reference/health-endpoints.md#warnungen-zum-zustand) hat, zeigt jede Seite ein Banner: in der Farbe der schwersten Warnung, mit der Zahl der Warnungen, der ersten Meldung und einem Link zu System. Bei einer einzelnen Warnung bekommen Admins außerdem einen Link dorthin, wo sie behoben wird. Schließ das Banner mit seiner Schaltfläche; es kommt wieder, wenn sich die Menge der Warnungen ändert. Das Banner aktualisiert sich live.

## Fehler

Jede fehlgeschlagene Anfrage zeigt eine Meldung in der Sprache deiner Oberfläche: neben dem Formular (deine Eingabe bleibt), als Zustand „konnte nicht geladen werden“ mit einem erneuten Versuch oder in einem globalen Banner. Eine abgelaufene Sitzung bringt dich mit der Meldung „Sitzung abgelaufen“ zur Anmeldeseite und danach zurück. Bei einem unerwarteten Serverfehler wird die Trace-ID angezeigt, damit du sie im Log findest.

## Darstellung und Sprache

Siehe [Sprache und Darstellung](language.md) und [als App installieren](install-as-app.md).

## Verwandte Seiten

::: cards
- [Benachrichtigungen](../notifications.md)
- [Beobachtbarkeit](../observability.md)
:::
