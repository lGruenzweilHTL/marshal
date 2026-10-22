---
icon: fa-solid fa-scale-balanced
description: "Bewerte die Antworten von Marshal, mach bewertete Durchläufe zu Eval-Fällen und spiel diese Fälle gegen ein anderes Modell, Template oder einen anderen Basis-Prompt ab, bevor du umstellst."
---

# Antworten bewerten und Eval-Fälle abspielen

Bewerte die Antworten von Marshal, mach bewertete Durchläufe zu Eval-Fällen und spiel diese Fälle gegen ein anderes Modell, Template oder einen anderen Basis-Prompt ab, bevor du umstellst.

## Bevor du anfängst

- Um Durchläufe zu bewerten, meldest du dich in der Weboberfläche an. Maschinen-Tokens können nicht bewerten.
- Um Fälle zu speichern und Eval-Läufe zu starten, brauchst du die Rolle `admin`.
- Mindestens ein konfiguriertes Modell pro Stufe, die du testen willst. Die Eval-Ansicht listet die Modelle aus `marshal.yaml`.

## Schritte

::: steps
1. **Schalte „Marshal beim Lernen helfen“ ein (optional)**

   Öffne **Präferenzen** und schalte **Marshal beim Lernen helfen** ein. Standardmäßig ist es aus. Solange es an ist, wird jeder Durchlauf, den du bewertest, ein Eval-Fall. Derselbe Schalter erlaubt auch die anderen Funktionen, die aus deinem Verhalten lernen; die Präferenzen listen sie.

   Bewertungen werden am Durchlauf gespeichert, egal ob der Schalter an oder aus ist.

2. **Bewerte Antworten**

   - **Im Chat:** Jede fertige Antwort zeigt einen Daumen hoch und einen Daumen runter. Daumen runter öffnet ein Notizfeld: Schreib, was hätte passieren sollen, zum Beispiel „hätte die Deckenleuchte ausschalten sollen, nicht die Stehlampe“. Die Notiz ist optional.
   - **Im Detail des Durchlaufs:** Öffne **Läufe**, dann einen Durchlauf. Die Bewertung steht unter den Fakten des Durchlaufs. So bewertest du Sprachdurchläufe im Nachhinein.

   Klicke die aktuelle Bewertung erneut an, um sie zu entfernen. Ein Eval-Fall aus einer Bewertung bleibt, wenn du die Bewertung entfernst.

   Um bewertete Durchläufe zu finden, filterst du die Liste der Läufe nach **Positiv bewertet** oder **Negativ bewertet**.

3. **Speichere Durchläufe als Fälle**

   Öffne als Admin einen Durchlauf und klicke auf **Als Eval-Fall speichern**. Das geht auch mit ausgeschaltetem Lernschalter und für Durchläufe ohne Bewertung; solche Fälle haben die Erwartung `unrated` und werden wie gute Fälle verglichen. Sobald ein Fall existiert, verlinkt das Detail des Durchlaufs darauf.

   Ein Fall speichert einen Turn:

   | Teil | Inhalt |
   |---|---|
   | Eingabe | Was du gesagt oder getippt hast, mit Eingabemodus, Sprache, Satellit und Bereich |
   | Verlauf | Die Unterhaltung vor dem Turn, nur Text von Benutzer und Assistent |
   | Turn-Kontext | Gefundenes Gedächtnis, Bereichskontext, Zeit und Aufgabenabschnitt, so wie das Modell sie gesehen hat |
   | Werkzeugaufrufe | Werkzeug-ID, Argumente und das aufgezeichnete Ergebnis jedes Aufrufs |
   | Antwort | Die Antwort von Marshal |
   | Erwartung | `good` (Daumen hoch), `bad` (Daumen runter) oder `unrated` |
   | Notiz | Was hätte passieren sollen |

   Ein Fall ist als **unvollständig** markiert, wenn aufgezeichnete Daten fehlen: das Ergebnis eines sensiblen Werkzeugs, das nur als Referenz behalten wurde, oder ein Durchlauf, der aufgezeichnet wurde, bevor Marshal den Turn-Kontext gespeichert hat.

4. **Bearbeite oder lösche Fälle**

   Öffne **System → Evals** (oder **Evals** in der Navigation) und klicke auf das Datum eines Falls. Bearbeite die Notiz **Erwartetes Verhalten** oder lösche den Fall.

5. **Starte einen Eval-Lauf**

   Wähle in **Evals** die Fälle aus und füll **Neuer Eval-Lauf** aus:

   - **Stufe Schnell, Standard und Advisor:** das zu testende Modell pro Stufe. **Aktuelles Routing** behält das Modell, das Marshal heute nutzt.
   - **Template:** die Fälle mit einem anderen [Agent-Template](agent-templates.md) ausführen.
   - **Basis-Prompt ersetzen:** ersetzt die Basisanweisungen von Marshal für die Wiedergabe.
   - **Bewertungsmodell:** optional. Ein Modell, das jede Antwort von 0 bis 1 gegen die aufgezeichnete Antwort und die Notiz des Falls bewertet.

   Klicke auf **Kosten schätzen**, um vor dem Start zu sehen, was der Lauf kostet, dann auf **Eval-Lauf starten**.
:::

## Wie eine Wiedergabe funktioniert

Jeder Fall wird mit der Kandidatenkonfiguration durch die Agentenschleife wiedergegeben. Verlauf, Eingabe und Turn-Kontext stammen aus dem Fall; Basisanweisungen, Skills und Werkzeugliste stammen aus der aktuellen Konfiguration plus deinen Ersetzungen.

In einer Wiedergabe läuft kein Werkzeug:

- Ein Aufruf, dessen Werkzeug und Argumente zu einem aufgezeichneten Aufruf passen, bekommt das aufgezeichnete Ergebnis. Die Reihenfolge der Schlüssel in den Argumenten spielt keine Rolle.
- Jeder andere Aufruf bekommt den Fehler „keine Aufzeichnung“, und der Fall ist `diverged`.

Wiedergaben schreiben nichts in Gedächtnis, Aufgaben, Benachrichtigungen oder Integrationen und erscheinen nicht in der Liste der Läufe. Aufrufe der Bestätigungsstufe erzeugen eine Bestätigungsfrage wie in einem interaktiven Turn, und nichts wird eingereiht.

## Den Bericht lesen

Der Bericht zeigt die Summen pro Ergebnis, die Kosten und pro Fall die aufgezeichneten und wiedergegebenen Werkzeugaufrufe und Antworten nebeneinander, mit der Notiz des Falls.

| Ergebnis | Guter oder unbewerteter Fall | Schlechter Fall |
|---|---|---|
| `pass` | Dieselben Werkzeuge mit denselben Argumenten; dieselbe Antwort oder eine Bewertung von mindestens 0.7 | Eine andere Folge von Werkzeugen |
| `changed` | Dieselben Werkzeuge, aber andere Argumente oder eine andere Antwort | Wiederholt die aufgezeichnete Folge von Werkzeugen |
| `diverged` | Ein Aufruf hatte keine Aufzeichnung | Ein Aufruf hatte keine Aufzeichnung |
| `failed` | Die Wiedergabe ist fehlgeschlagen (Fehler, Schrittlimit oder Kostengrenze) | Ebenso |

Ein behobener schlechter Fall zeigt sich meist als `diverged`: Der richtige Aufruf wurde nie aufgezeichnet. Lies die wiedergegebenen Aufrufe neben der Notiz, um die Korrektur zu beurteilen. Ein zusätzlicher harmloser Aufruf, etwa ein Statuslesen, macht einen Fall ebenfalls `diverged`.

Eval-Läufe und ihre Ergebnisse bleiben, bis du sie löschst. Einen wartenden oder laufenden Eval-Lauf kannst du abbrechen.

## Kosten

Wiedergaben und Aufrufe des Bewertungsmodells nutzen echte Modelle. Ihre Kosten zählen gegen `tasks.dailyCostCap`; ein Eval-Lauf endet als `failed`, wenn die Grenze ausgeschöpft ist. Die Schätzung zählt den aufgezeichneten Text jedes Falls mit etwa vier Zeichen pro Token, einmal pro Modellaufruf gesendet, mit den Preisen der Kandidatenmodelle.

## Datenschutz

Eval-Fälle behalten den Text des bewerteten Turns (Eingabe, Verlauf, Turn-Kontext, Werkzeugergebnisse und Antwort) über die Aufbewahrung der Läufe hinaus, bis du sie löschst. Sie entstehen nur, wenn du mit eingeschaltetem „Marshal beim Lernen helfen“ bewertest oder wenn ein Admin einen Durchlauf speichert. Ergebnisse sensibler Werkzeuge bleiben Referenzen. Siehe [Datenschutz](../concepts/privacy.md#aufbewahrung).

## Prüfen, ob es funktioniert

1. Bewerte eine Chat-Antwort mit Daumen runter und einer Notiz.
2. Öffne als Admin den Durchlauf und klicke auf **Als Eval-Fall speichern** (oder prüfe, dass der Durchlauf auf seinen Fall verlinkt).
3. Wähle in **Evals** den Fall, klicke auf **Kosten schätzen** und dann auf **Eval-Lauf starten**.
4. Der Bericht zeigt ein Ergebnis mit den aufgezeichneten und wiedergegebenen Aufrufen.

## Verwandte Seiten

::: cards
- [API: Endpunkte für Evals](../api/index.md#interne-endpunkte-der-spa)
- [Beobachtbarkeit](observability.md)
  Für Durchläufe und Kosten
- [`tasks.dailyCostCap`](../configuration/marshal-yaml/tasks.md)
:::
