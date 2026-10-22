---
icon: fa-solid fa-flask
description: "Demo-Modus: simulierte Integrationen und Beispielverlauf für eine Instanz, in der nur ein Modell konfiguriert ist."
---

# `demo`

Demo-Modus: simulierte Integrationen und Beispielverlauf für eine Instanz, in der nur ein Modell konfiguriert ist.

## Beispiel

```yaml
demo:
  enabled: true
  showBadge: true
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `demo.enabled` | Boolean | `false` | Simuliert jede von Home Assistant, Druckern, Mail, FreshRSS und Notizen, die nicht konfiguriert ist, und legt einmal einen Beispielverlauf für den Eigentümer der Instanz an. |
| `demo.showBadge` | Boolean | `true` | Zeigt das Abzeichen „Demo“ in der Navigation. Setz es für Screenshots auf `false`. Die Ansicht System markiert simulierte Integrationen in jedem Fall. |

Eine Integration gilt als konfiguriert, wenn ihr echter Client sie nutzen würde:

| Integration | Konfiguriert, wenn |
|---|---|
| Home Assistant | `ha.url` gesetzt ist |
| Drucker | `printers` mindestens einen Eintrag hat |
| Mail | der Eigentümer ein Mailkonto hat (bis dahin erscheint das Demokonto `demo`) |
| FreshRSS | `freshrss.url`, `freshrss.userName` und das API-Passwort auflösbar sind |
| Notizen | `notes.remote` gesetzt ist oder das Notizverzeichnis existiert |

Der Demo-Modus ersetzt das Modell nicht: Ohne `models` scheitert der Start weiterhin. Simulierte Integrationen kontaktieren nie einen Netzwerk-Host, und die simulierte Mail hat keinen Sendeweg.

## Validierung

Der Abschnitt hat keinen Validator.

## Verwandte Seiten

::: cards
- [Marshal im Demo-Modus ausprobieren](../../guides/demo-mode.md)
- [`marshal demo`](../../reference/cli.md#marshal-demo)
- [`integrations`](integrations.md)
:::
