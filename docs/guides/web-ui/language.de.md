---
icon: fa-solid fa-language
description: "Stell Sprache und Zeitzone der Oberfläche für dein Konto ein und das Farbschema für ein Gerät."
---

# Sprache, Zeitzone und Darstellung ändern

Stell Sprache und Zeitzone der Oberfläche für dein Konto ein und das Farbschema für ein Gerät.

## Bevor du anfängst

- Melde dich in der Weboberfläche an.

## Schritte

::: steps
1. **Öffne die Präferenzen**

   Öffne **Einstellungen > Präferenzen**.

2. **Sprache und Region**

   - **Sprache:** Deutsch (`de`) oder Englisch (`en`). Die Oberfläche wechselt ohne Neuladen der Seite.
   - **Zeitzone:** eine IANA-ID wie `Europe/Vienna`. Daten, Uhrzeiten und Zeitpläne nutzen sie. Eine ungültige ID wie `Mars/Base` wird abgewiesen.

3. **Darstellung**

   Wähle **System**, **Hell** oder **Dunkel**. System folgt deinem Betriebssystem. Diese Einstellung wird nur im Browser auf diesem Gerät gespeichert, gilt sofort und wird nie an Marshal gesendet. Ein anderer Browser behält seine eigene Wahl.
:::

## Was sich ändert

- Jeder Text der Oberfläche existiert auf Deutsch und Englisch. Daten, Uhrzeiten, Zahlen und Dauern folgen der Sprache und deiner Zeitzone.
- Inhalte, die du oder das Modell geschrieben habt (Gedächtniseinträge, Chat-Nachrichten, Benachrichtigungen), bleiben, wie sie gespeichert sind, und werden nicht übersetzt.
- Durchläufe, die ohne Sprache der Anfrage ankommen, nutzen deine Sprachpräferenz.
- Bevor du dich anmeldest, folgt die Anmeldeseite der Sprache deines Browsers und fällt auf Englisch zurück.

## Standardwerte

- Für den Eigentümer der Instanz beginnt die Sprache mit `owner.language` aus `marshal.yaml`. Siehe [`owner`](../../configuration/marshal-yaml/owner.md).
- Die Zeitzone eines neuen Benutzers beginnt mit `instance.timeZone` (oder der Zeitzone von Home Assistant, sonst UTC). Siehe [`instance`](../../configuration/marshal-yaml/instance.md).

> [!NOTE]
> Präferenzen enthalten Sprache, Zeitzone, Ruhezeiten, Benachrichtigungsziele und Nicht stören. Weichere Wünsche, etwa Themen für das Briefing, gehören ins Gedächtnis: Sag es Marshal. Siehe [Gedächtnis](../memory.md).

## Verwandte Seiten

::: cards
- [Benachrichtigungen](../notifications.md)
  Für Ruhezeiten
- [Rundgang durch die Weboberfläche](tour.md)
:::
