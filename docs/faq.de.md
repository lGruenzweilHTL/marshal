---
icon: fa-solid fa-circle-question
order: 100
description: "Kurze Antworten auf häufige Fragen dazu, was Marshal ist, was es kann und was es absichtlich nicht kann."
---

# Häufige Fragen

Kurze Antworten auf häufige Fragen dazu, was Marshal ist, was es kann und was es absichtlich nicht kann.

## Allgemein

::: details Gibt es ein Release oder ein veröffentlichtes Image?
Nein. Marshal hat noch kein Release. Du baust das Image aus dem Repository. Siehe [Schnellstart](getting-started/quick-start.md).
:::

::: details Kann ich Marshal ausprobieren, ohne meine Dienste zu verbinden?
Ja. Schalte den [Demo-Modus](guides/demo-mode.md) ein. Jede Integration, die du nicht konfiguriert hast, wird dann simuliert, und die Ansichten zeigen einen Beispielverlauf. Ein Chat-Modell brauchst du trotzdem.
:::

::: details Braucht Marshal die Cloud?
Nein. Marshal läuft auf deiner eigenen Hardware und funktioniert mit vollständig lokalen Modellen über Ollama oder einen lokalen OpenAI-kompatiblen Server. Nur die Dienste, die du verbindest, etwa ein Cloud-Modell oder ein Gmail-Konto, werden über das Internet erreicht. Siehe [Modelle einrichten](installation/models-setup.md).
:::

::: details Welche Sprachen unterstützt es?
Die Web-App und der Agent funktionieren auf Englisch und Deutsch. Siehe [Sprache, Zeitzone und Darstellung ändern](guides/web-ui/language.md).
:::

::: details Können mehrere Personen eine Instanz teilen?
Noch nicht. Unterstützt wird ein Benutzer pro Instanz. Das Datenmodell ist für mehr vorbereitet: Jede Zeile, die einem Benutzer gehört, hat einen Eigentümer. Siehe [Datenbesitz](concepts/data-ownership.md).
:::

## Sicherheit

::: details Warum kann Marshal keine Mails senden?
Absichtlich. Marshal liest Mails und bereitet Entwürfe in deinem Entwurfsordner vor, hat aber keinen Sendepfad: keine SMTP-Konfiguration und keine Sende-API. Du prüfst den Entwurf und sendest ihn selbst. Siehe [Ein Mailkonto verbinden](guides/integrations/mail.md).
:::

::: details Wie entscheidet Marshal, wann es mich zuerst fragt?
Jedes Werkzeug hat eine Risikostufe. Harmlose Werkzeuge laufen, riskante brauchen deine Bestätigung, und verbotene Werkzeuge sieht das Modell überhaupt nicht. Siehe [Risiko und Bestätigung](concepts/risk-and-confirmation.md) und [Risikoregeln](guides/risk-rules.md).
:::

::: details Kann eine Prompt Injection in einer Mail oder Webseite Marshal zum Handeln bringen?
Inhalte aus Mail, RSS, Notizen und Webseiten sind nicht vertrauenswürdige Daten. Ein Durchlauf, der sie gelesen hat, ist belastet, und seine Schreibvorgänge in Gedächtnis und Aufgaben brauchen eine Bestätigung. Kein Werkzeug ruft eine URL ab, die das Modell gewählt hat. Siehe [Nicht vertrauenswürdige Inhalte](concepts/untrusted-content.md).
:::

::: details Wo sind meine Secrets?
Nicht in `/config`. Die Konfiguration enthält nur Secret-Namen; die Werte kommen aus Umgebungsvariablen, Dateien unter `/run/secrets` oder der verschlüsselten Datenbank. Der Start scheitert, wenn er ein wörtliches Secret findet. Siehe [Secrets verwalten](guides/secrets.md).
:::

## Betrieb

::: details Marshal startet nicht. Was jetzt?
Führ `docker compose logs marshal` aus. Der Start listet jedes Konfigurationsproblem mit Datei und Schlüssel. Siehe [Fehlerbehebung](guides/troubleshooting.md).
:::

::: details Wo liegen meine Daten, und wie sichere ich sie?
Alles, was Marshal speichert, liegt in `/data`: die SQLite-Datenbank, die Schlüssel für die Verschlüsselung, Backups und das Notiz-Repository. Siehe [Backups und Wiederherstellung](guides/backups-and-restore.md) und [Volumes und Aufbau](installation/volumes-and-layout.md).
:::

::: details Ändert Marshal meine Server, meinen Code oder meine Notizen?
Nein. Die Integrationen für Proxmox und GitHub sind nur lesend. Für Notizen und Code bereitet Marshal Änderungsvorschläge vor, die du prüfst und freigibst. Siehe [Proxmox](guides/integrations/proxmox.md), [GitHub](guides/integrations/github.md) und [Notizen](guides/integrations/notes.md).
:::

::: details Kann Marshal Code ausführen?
Nur über den optionalen Executor-Host, einen eigenen Dienst, der Coding-Sitzungen in Sandbox-Containern ausführt. Marshal hat nie Zugriff auf einen Docker-Socket. Siehe [Executor-Host](installation/executor-host.md).
:::

## Integration

::: details Welche Versionen von Home Assistant funktionieren?
Siehe [Unterstützte Versionen](reference/supported-versions.md).
:::

::: details Kann ich Marshal ohne Sprachsteuerung über Home Assistant nutzen?
Ja. Die Web-App und die [API](api/index.md) funktionieren für sich, auch wenn die meisten Integrationen Home Assistant voraussetzen.
:::

::: details Kann ich eigene Werkzeuge hinzufügen?
Ja, über MCP-Server. Siehe [Einen eigenen MCP-Server hinzufügen](guides/integrations/custom-mcp-servers.md). Werkzeuge von MCP-Servern, auf die keine Regel passt, stehen standardmäßig in der Bestätigungsstufe.
:::
