---
icon: fa-brands fa-github
description: "Gib Marshal Lesezugriff auf eine kurze Liste von GitHub-Repositorys, damit es Pull Requests und Issues zusammenfasst und fehlgeschlagene CI-Läufe erklärt."
---

# Pull Requests, Issues und CI-Läufe auf GitHub lesen

Gib Marshal Lesezugriff auf eine kurze Liste von GitHub-Repositorys, damit es Pull Requests und Issues zusammenfasst und fehlgeschlagene CI-Läufe erklärt.

## Bevor du anfängst

- Ein GitHub-Konto, das die Repositorys lesen kann, die du freigeben willst.
- Ein Weg, ein Secret zu speichern, siehe [Secrets](../secrets.md).

## Schritte

::: steps
1. **Erstelle ein Fine-grained Personal Access Token**

   Öffne in GitHub **Settings > Developer settings > Personal access tokens > Fine-grained tokens** und erstelle ein Token:

   - **Repository access:** Wähle nur die Repositorys, die du erlauben willst.
   - **Permissions**, alle nur lesend: Metadata, Contents, Pull requests, Issues, Actions.
   - **Expiration:** GitHub erlaubt höchstens ein Jahr.

   > [!WARNING]
   > Nimm für den Coding-Executor von Marshal ein eigenes Token. Dieses Token darf nie Schreibzugriff haben.

2. **Speichere das Token als Secret**

   Leg ein Secret wie `github_pat` an, das das Token enthält. Läuft das Token ab, ersetzt du den Wert des Secrets; Marshal liest es bei jeder Anfrage und braucht keinen Neustart.

3. **Füg den Abschnitt `github` hinzu**

   ```yaml
   github:
     tokenSecret: github_pat
     repos:
       marshal: lukas/marshal
       homelab: lukas/homelab
   ```

   Jeder Schlüssel unter `repos` ist die ID, die das Modell sieht. Sie passt auf `[a-z][a-z0-9-]*`. Jeder Wert ist `owner/name`. Der Start schlägt mit einer dieser Meldungen fehl, wenn eine Regel verletzt ist:

   - `github.repos ids must be lowercase letters, digits and dashes, starting with a letter.`
   - `github.repos values must be owner/name.`
   - `github.tokenSecret must name a secret when repos are configured.`

   Ohne Abschnitt `github` oder mit leerem `repos` registriert Marshal kein Werkzeug `repos_*`. Starte Marshal nach dem Bearbeiten der Datei neu.
:::

## Werkzeuge

Alle Werkzeuge sind in der Stufe safe. Jedes Werkzeug nimmt ein Argument `repo`. Sein Schema ist ein Enum deiner Repo-IDs, und die Beschreibung des Werkzeugs zeigt zu jeder ID `owner/name`.

| Werkzeug | Was es tut |
|---|---|
| `repos_prs(repo, state?)` | Bis zu 30 neueste Pull Requests: Nummer, Titel, Autor, Draft-Flag, Head- und Base-Branch, Änderungszeit. `state` ist `open` (Standard), `closed` oder `all`. |
| `repos_pr(repo, number)` | Titel, Autor, Zustand, Draft- und Merged-Flag, Branches, Text (bei 4000 Zeichen abgeschnitten), geänderte Dateien, Hinzufügungen, Löschungen und die ID eines Artefakts `diff`. |
| `repos_issues(repo, state?, query?)` | Bis zu 30 neueste Issues, ohne Pull Requests: Nummer, Titel, Autor, Labels, Zahl der Kommentare, Änderungszeit. `query` filtert Titel unter den 100 neuesten, ohne Beachtung der Groß- und Kleinschreibung. |
| `repos_issue(repo, number)` | Titel, Autor, Zustand, Labels, Text (bei 4000 Zeichen abgeschnitten) und die 20 neuesten Kommentare (jeder bei 1000 Zeichen abgeschnitten). |
| `repos_runs(repo, branch?)` | Bis zu 20 neueste Workflow-Läufe: ID, Workflow, Titel, Branch, Ereignis, Status, Ergebnis, kurzer SHA, Zeit. |
| `repos_run_log(repo, runId)` | Für jeden fehlgeschlagenen Job: Name, Namen der fehlgeschlagenen Schritte, die letzten 50 Log-Zeilen und die ID eines Artefakts `code` mit dem vollständigen Log. |

Ein Werkzeug scheitert mit `repos.not_found` bei einer unbekannten Nummer oder Lauf-ID. Es scheitert mit `repos.unavailable`, wenn GitHub nicht erreichbar ist oder das Token ablehnt, und nennt die Zeit des Zurücksetzens, wenn das Rate Limit ausgeschöpft ist.

### Diffs und Logs als Artefakte

`repos_pr` speichert den Diff des Pull Requests als Artefakt `diff` mit einem Namen wie `marshal-pr-42.diff`. `repos_run_log` speichert das vollständige Log jedes fehlgeschlagenen Jobs als Artefakt `code`. Du kannst sie in der Weboberfläche öffnen oder auf einem Display zeigen, siehe [Artefakte](../artifacts.md) und [Displays](../displays.md).

- Artefakte gehören dem Eigentümer des Durchlaufs, haben die Quelle `github` und sind als belastet markiert.
- Ein wiederholter Aufruf für denselben Commit eines Pull Requests oder denselben Job liefert die ID des vorhandenen Artefakts, statt ein neues zu speichern. Ein neuer Commit am Pull Request speichert einen neuen Diff.
- Ist das Kontingent des Eigentümers oder die Größengrenze überschritten, liefert das Werkzeug trotzdem seine Zusammenfassung und sagt, dass der Diff oder das Log nicht gespeichert wurde.

## Grenzen

- Marshal schickt nur `GET`-Anfragen. Es kann nicht kommentieren, reviewen, mergen, schließen, labeln, bearbeiten, Workflows starten oder neu ausführen und keine Einstellungen von Repositorys ändern.
- Ein Repo außerhalb der Allowlist scheitert mit `repos.invalid_arguments`, und keine Anfrage erreicht GitHub. Anfragen gehen nur an das konfigurierte `owner/name` der angegebenen ID.
- Alles, was die Werkzeuge liefern, ist Text von Dritten. Ein Durchlauf, der eines davon aufruft, ist mit der Quelle `github` belastet. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).
- Werkzeugergebnisse werden wie oben gelistet gekürzt, sehr lange Texte und Kommentare werden also verkürzt.

## Prüfen, ob es funktioniert

1. Frag „Welche Pull Requests sind in marshal offen?“ Die Antwort listet offene Pull Requests.
2. Bitte „Zeig mir den Diff von PR 42“. Die Antwort fasst den PR zusammen und verlinkt ein Artefakt `diff`; öffne es im Artefakt-Viewer.
3. Frag „Warum ist der letzte CI-Lauf fehlgeschlagen?“ Die Antwort nennt den fehlgeschlagenen Job, seine fehlgeschlagenen Schritte und die letzten Log-Zeilen.
4. Frag nach einem Repository, das nicht in deiner Liste steht. Marshal sieht es nicht und kann es nicht lesen.

Scheitert jeder Aufruf mit `repos.unavailable`, prüfe, dass das Token nicht abgelaufen ist und das Repository abdeckt.

## Verwandte Seiten

::: cards
- [`github` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Artefakte](../artifacts.md)
- [Secrets](../secrets.md)
- [Executor-Host](../../installation/executor-host.md)
- [Werkzeug-Referenz](../../reference/tools.md)
:::
