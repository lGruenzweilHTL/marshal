---
icon: fa-solid fa-envelope
description: "Gib Marshal über IMAP Lesezugriff auf deine Mail und lass es Entwürfe vorbereiten, ohne jede Möglichkeit, etwas zu senden, zu löschen oder zu verschieben."
---

# Ein Mailkonto verbinden

Gib Marshal über IMAP Lesezugriff auf deine Mail und lass es Entwürfe vorbereiten, ohne jede Möglichkeit, etwas zu senden, zu löschen oder zu verschieben.

## Bevor du anfängst

- Ein IMAP-Postfach: ein beliebiger Anbieter, Gmail oder Microsoft 365 / Outlook.com.
- Eine dieser Zugangsarten:
  - **Passwort oder App-Passwort.** Erstelle bei deinem Anbieter ein App-Passwort, wenn er welche anbietet (Gmail mit Bestätigung in zwei Schritten, iCloud, Fastmail und andere). Nimm nicht dein Hauptpasswort, wo es ein App-Passwort gibt.
  - **Google-Anmeldung** oder **Microsoft-Anmeldung** (OAuth2 mit XOAUTH2). Das braucht einen OAuth-Client, den der Admin der Instanz einmal registriert.
- IMAP-Host, Port und Sicherheit (993 mit TLS oder 143 mit STARTTLS).

## Schritte

::: tabs
== Passwort oder App-Passwort
1. Öffne in der Weboberfläche **Mailkonten**.
2. Gib unter **IMAP-Konto hinzufügen** einen Alias (1 bis 32 Kleinbuchstaben, Ziffern oder `-`, etwa `private` oder `work`), Host, Port, Sicherheit, Benutzernamen und Passwort ein.
3. Optional: Setz einen **Entwürfe-Ordner**. Du brauchst ihn nur, wenn der Server keinen Ordner als `\Drafts` markiert.
4. Wähle **Konto hinzufügen**.

Das Passwort wird ein nur schreibbares Secret deines Benutzers. Es wird verschlüsselt gespeichert und nie wieder angezeigt. Konten liegen in der Datenbank, nicht in `marshal.yaml`.
== Google- oder Microsoft-Anmeldung
1. Registriere beim Anbieter einen OAuth-Client. Redirect-URI: `<public URL>/oauth/callback/google` oder `<public URL>/oauth/callback/microsoft`.
   - Google braucht den Scope `https://mail.google.com/`.
   - Microsoft braucht `IMAP.AccessAsUser.All` und `offline_access`.
2. Speichere jedes Client-Secret als Secret und nenne es in `marshal.yaml`:

```yaml [/config/marshal.yaml]
oauth:
  google: { clientId: "<id>.apps.googleusercontent.com", clientCredentialSecret: google_oauth_client }
  microsoft: { clientId: "<application id>", clientCredentialSecret: microsoft_oauth_client }
```

3. Starte Marshal neu. Ohne Client ist die Verbinden-Schaltfläche für diesen Anbieter ausgegraut.
4. Gib in **Mailkonten** unter **Per Anmeldung verbinden** einen Alias und deine Adresse ein, wähle dann **Google verbinden** oder **Microsoft verbinden** und melde dich an.

Marshal behält nur das Refresh-Token, als nur schreibbares Secret deines Benutzers. Die IMAP-Hosts sind `imap.gmail.com` und `outlook.office365.com`, beide auf Port 993 mit TLS. Die Schlüssel stehen in der [Referenz von `oauth`](../../configuration/marshal-yaml/integrations.md#oauth-verbindungen-von-mailkonten).
:::

### Ein Konto entfernen

Wähle beim Konto **Entfernen**. Marshal kann es dann nicht mehr lesen und löscht die gespeicherten Zugangsdaten.

## Werkzeuge

| Werkzeug | Stufe | Was es tut |
|---|---|---|
| `mail.search(account?, from, to, subject, text, since, before, unread, folder, limit)` | safe | Durchsucht ein Konto, oder alle deine, wenn `account` fehlt. Ergebnisse tragen den Alias. 1 bis 50 Ergebnisse. Der Ordner ist standardmäßig der Posteingang. |
| `mail.read_message(id)` | safe | Eine Nachricht als Text. Anhänge werden nur mit Namen gelistet. |
| `mail.read_thread(id)` | safe | Die ganze Unterhaltung, die älteste zuerst. Bei IMAP wird der Thread aus `References` und `In-Reply-To` rekonstruiert. |
| `mail.list_folders(account?)` | safe | Ordner eines Kontos. |
| `mail.list_labels(account?)` | safe | Labels. Verborgen, solange kein Konto Labels unterstützt. |
| `mail.create_draft(account, to, subject, body, replyTo?)` | safe | Speichert einen Entwurf. Nichts wird gesendet. |
| `mail.update_draft(id, to, subject, body, replyTo?)` | safe | Ersetzt einen Entwurf, den Marshal angelegt hat. |
| `mail.save_attachment(message, attachment)` | confirm | Speichert einen Anhang als Artefakt. `attachment` ist sein Name oder seine Position. Verborgen, solange du kein Mailkonto hast. |

`search`, `read_message` und `read_thread` liefern Text von Dritten, ihre Ausgabe ist also nicht vertrauenswürdig und sensibel. Mail lesen belastet den Durchlauf mit der Quelle `mail`, und die Aufzeichnung des Durchlaufs speichert nur den Verweis auf die Nachricht. In einem belasteten Durchlauf brauchen Schreibvorgänge in Gedächtnis und Aufgaben eine Bestätigung, mit einem Grund, der Mail nennt. Entwurfswerkzeuge werden durch Belastung nicht hochgestuft, weil ein Entwurf nichts bewirken kann, bis du ihn selbst sendest.

`mail.save_attachment` fragt immer nach einer Bestätigung. Es holt nur diesen Anhang und speichert ihn als belastetes Artefakt (Quelle `mail`). Gibt es den Namen nicht, scheitert es und nennt die Anhänge der Nachricht. Siehe [Artefakte](../artifacts.md).

### Entwürfe

Marshal schreibt Entwürfe mit IMAP `APPEND` in den als `\Drafts` markierten Ordner (RFC 6154), mit dem Flag `\Draft`. Ein deutscher Ordner „Entwürfe“ funktioniert, wenn der Server ihn markiert. Ist kein Ordner markiert und nennt das Konto keinen, scheitert das Werkzeug mit einem klaren Fehler. Du öffnest den Entwurf in deinem Mailprogramm, prüfst ihn und sendest ihn selbst.

## Warum es keinen Sendeweg gibt

Marshal liest Mail und entwirft Antworten. Es kann keine Nachricht senden, löschen, in den Papierkorb legen, markieren, labeln oder verschieben, und das ist eine Designregel, keine fehlende Funktion.

- Mail ist der wichtigste Weg, auf dem Außenstehende dem Modell Text vorlegen. Eine Nachricht kann Anweisungen verstecken. Könnte dasselbe Modell Mail senden, könnte eine einzige feindliche Nachricht Marshal dazu bringen, in deinem Namen Mail abfließen zu lassen oder zu fälschen.
- Marshal enthält keine SMTP-Konfiguration, keinen SMTP-Client, keinen Aufruf `messages.send` oder `drafts.send` und keinen Graph-Scope `Mail.Send`. Nichts, was ein Prompt sagt, kann freischalten, was der Code nicht enthält.
- Ein Entwurf stellt einen Menschen zwischen Modell und Empfänger. Du prüfst ihn und drückst auf Senden.
- Die OAuth-Anmeldung fragt nach IMAP-Zugriff, nicht nach Senderechten.

Die Werkzeugliste enthält kein Werkzeug, das eine Nachricht senden, löschen oder verschieben kann, egal welche Konten du hinzufügst.

## Grenzen

- Nur IMAP. Gmail und Microsoft funktionieren über IMAP mit XOAUTH2.
- Deine Konten sind privat für deinen Benutzer. Jede Kontozeile hat einen Eigentümer.
- Reiner Text in Nachrichten; Entwürfe sind reiner Text.
- Ereignisse zu neuer Mail kommen aus IMAP IDLE (alle 9 Minuten erneuert) mit einer Abfrage alle 2 Minuten als Rückfall.

## Prüfen, ob es funktioniert

1. **Mailkonten** listet das Konto mit seiner Zugangsart.
2. Frag „habe ich ungelesene Mail von der Bank?“ Das Modell ruft `mail_search` auf, und die Antwort nennt den Alias des Kontos.
3. Bitte „entwirf eine Antwort auf Annas letzte Mail, dass ich um 6 da bin“. Ein Entwurf erscheint in deinem Entwürfe-Ordner. Nichts wird gesendet.

## Fehlerbehebung

- **Zugangsdaten fehlen.** Das gespeicherte Secret ist weg. Entferne das Konto und füge es neu hinzu.
- **Fehler beim Entwürfe-Ordner.** Setz den **Entwürfe-Ordner** am Konto.
- **Verbinden-Schaltfläche ausgegraut.** Für diesen Anbieter ist kein OAuth-Client konfiguriert.
- **„Die Anmeldung ist abgelaufen“.** Starte die Verbindung im selben Browser neu.

Siehe [Fehlerbehebung](../troubleshooting.md).

## Verwandte Seiten

::: cards
- [Nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md)
- [Secrets](../secrets.md)
- [Tägliches Briefing](../tasks/daily-briefing.md)
- [Risikostufen](../../reference/risk-tiers.md)
- [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md)
:::
