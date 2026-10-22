---
icon: fa-solid fa-eye
description: "Warum Marshal Werkzeuge vor dem Modell verbirgt, statt sie abzulehnen, und welche Regeln entscheiden, was das Modell sieht."
---

# Sichtbarkeit von Werkzeugen

Diese Seite erklärt, warum Marshal Werkzeuge vor dem Modell verbirgt, statt sie abzulehnen, und welche Regeln entscheiden, was das Modell sieht.

## Das Problem

Der übliche Weg, einen Agenten von etwas abzuhalten, ist, den Aufruf abzulehnen, nachdem das Modell ihn gemacht hat. Das hat Kosten:

- Das Modell verschwendet Schritte und Latenz an Aufrufe, die nie gelingen können.
- Ablehnungen laden das Modell ein, zu argumentieren, mit anderen Argumenten neu zu versuchen oder einen Umweg zu suchen.
- Jedes Werkzeug in der Liste kostet Prompt-Tokens und Aufmerksamkeit, und kleine lokale Modelle wählen aus langen Listen schlechter.
- Ein Werkzeug, das das Modell sehen kann, ist ein Ziel für Prompt Injection, auch wenn es später abgelehnt wird.

## Die Regel

**Was das Modell nicht nutzen darf, sieht es nicht.** Verbotene, verweigerte oder nicht verfügbare Werkzeuge werden vor jedem Modellaufruf aus der Werkzeugliste gefiltert. Der Guard setzt dieselben Regeln als zweite Linie bei jedem Aufruf noch einmal auf dem Server durch.

## Werkzeug-IDs und Namen

Jedes Werkzeug hat eine interne ID `<namespace>.<name>`, die in Risikoregeln, Satellitenrichtlinien, Allowlists von Templates, Aufzeichnungen der Durchläufe und im Audit-Log steht. Das Modell sieht einen Namen `<namespace>_<name>`. Die Registry bildet in beide Richtungen ab: Das Modell ruft `agent_ask_advisor` auf, Marshal löst `agent.ask_advisor` auf.

Namensräume benennen Fähigkeiten, nie Anbieter. Diese Namensräume sind für eingebaute Werkzeuge reserviert: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music` und `system`. Ein MCP-Server mit der ID `mail` oder `music` wird abgewiesen.

Namen externer Werkzeuge werden bereinigt: Zeichen außerhalb von `[a-zA-Z0-9_-]` werden zu `_`, und Namen über 64 Zeichen werden mit einem Hash-Suffix gekürzt. Bilden zwei Werkzeuge auf denselben Namen für das Modell ab, wird die später geladene Werkzeugquelle abgewiesen. Siehe [Werkzeugreferenz](../reference/tools.md).

## Der Sichtbarkeitsfilter

Vor jedem Modellaufruf entfernt Marshal ein Werkzeug aus der Liste, wenn eine dieser Regeln greift, in dieser Reihenfolge:

| Regel | Beispiel |
|---|---|
| Nicht in der `tools`-Allowlist des aktiven Templates | Ein Template mit `tools: [printer.*, agent.load_skill]` sieht nur Druckerwerkzeuge und das Laden von Skills. |
| Durch eine Risikoregel für jeden Aufruf verboten | Eine Regel in `risk.yaml` ohne Bedingungen markiert `ha.intent__HassClimateSetTemperature` als `forbidden`. |
| Für den Satelliten verweigert (`denyTools`) | Der Satellit im Kinderzimmer hat `denyTools: [mail.*]`. |
| Nur mit Bestätigung, in einem Subagenten | Ein Subagent hat niemanden zum Fragen, also werden Werkzeuge, die immer eine Bestätigung brauchen, verborgen. |
| Nur mit Bestätigung, auf einem Satelliten mit `denyConfirm` dafür | Ein Satellit im Flur mit `denyConfirm: [printer.*]` sieht keine Druckerwerkzeuge, die nur mit Bestätigung laufen. |
| Die eigene Sichtbarkeitsbedingung des Werkzeugs schlägt fehl | `agent.ask_advisor` in Sprach-Turns; `mail.list_labels`, wenn kein Konto Labels unterstützt; Werkzeuge eines MCP-Servers, der ausgefallen ist; `system.health` ohne Warnungen. |
| Das MCP-Werkzeug wartet auf Freigabe | Ein Werkzeug, das nach der ersten Erkennung seines Servers neu ist oder sich geändert hat, bis ein Admin es freigibt ([Werkzeug-Pinning](../configuration/mcp-servers.md#werkzeug-pinning)). Der Guard verweigert Aufrufe daran ebenfalls. |

„Nur mit Bestätigung“ schließt die Anhebung durch Belastung ein. In einem belasteten Durchlauf zählt ein Werkzeug mit `persists`, das `safe` wäre, als `confirm`, es verschwindet also in Subagenten und auf Satelliten, die Bestätigungen dafür verweigern.

### Argumentabhängige Werkzeuge bleiben sichtbar

Manche Werkzeuge sind für manche Argumente `safe` und für andere `confirm`. Ein Licht einzuschalten ist sicher, eine Tür aufzusperren braucht eine Bestätigung, und beides läuft über dasselbe Home-Assistant-Werkzeug. Ein solches Werkzeug zu verbergen würde auch seine sicheren Nutzungen verbergen. Diese Werkzeuge bleiben sichtbar, und der Guard entscheidet pro Aufruf: Ein Aufruf, der auf einem Satelliten mit `denyConfirm` zu `confirm` wird, wird mit „not allowed from this device“ verweigert, und keine ausstehende Aktion entsteht.

## Werkzeugsuche

Eine lange Werkzeugliste kostet bei jedem Modellaufruf Tokens und macht es einem Modell, besonders einem lokalen, schwerer, das richtige Werkzeug zu wählen. Nach dem Sichtbarkeitsfilter bietet Marshal deshalb nur die Werkzeuge der Namensräume an, die eine Sitzung geladen hat:

- **Kern-Namensräume** (`tools.core`, standardmäßig `agent`, `memory`, `tasks`, `notifications`, `timers`, `places`) sind immer geladen.
- **Suche.** Vor dem ersten Modellaufruf eines Turns vergleicht Marshal das Embedding des Turns (das die Gedächtnissuche ohnehin berechnet) mit einer einzeiligen Beschreibung jedes anderen Namensraums und lädt die besten Treffer über `tools.retrieval.threshold`, höchstens `tools.retrieval.maxPerTurn`. Eingebaute Werkzeuge beschreiben ihre Namensräume im Code; ein MCP-Server kann `description` in seiner Datei setzen.
- **Hinweise.** Deterministische Treffer laden einen Namensraum ohne Embedding: Eine Home-Assistant-Entität, ein Alias oder ein Bereich im Turn lädt `ha`, ein konfigurierter Lautsprechername lädt `music`.
- **`agent.find_tools(query)`.** Vermisst das Modell ein Werkzeug, kann es danach fragen. Das Werkzeug lädt die am besten passenden Namensräume unter denen mit sichtbaren Werkzeugen; die gefundenen Werkzeuge werden ab dem nächsten Modellaufruf angeboten.
- **Direkte Aufrufe.** Ein Aufruf eines sichtbaren Werkzeugs aus einem noch nicht geladenen Namensraum lädt ihn und geht wie gewohnt durch den Guard.

Geladene Namensräume bleiben für den Rest der Sitzung geladen. Neue Werkzeuge werden ans Ende der Liste angehängt (zuerst die Kern-Namensräume, dann die Ladereihenfolge, Werkzeuge nach Name), das gecachte Prompt-Präfix des Anbieters bleibt also erhalten. Die Suche entfällt, und jedes sichtbare Werkzeug wird angeboten, wenn `tools.retrieval.enabled` auf `false` steht oder höchstens `tools.retrieval.minTools` Werkzeuge sichtbar sind, etwa bei einem engen Template.

Die Suche ist ein Mittel zur Effizienz, keine Grenze. Sie bietet nie ein Werkzeug an, das der Filter entfernt hat, und `agent.find_tools` nennt nur Namensräume mit sichtbaren Werkzeugen, kann also kein Werkzeug verraten, das ein Satellit oder Template verbirgt. Die Detailansicht eines Laufs listet jeden geladenen Namensraum mit Grund und Wert. Siehe [`tools`](../configuration/marshal-yaml/tools.md).

### Gesprochene Überbrückung

Ein Sprach-Turn ist still, während ein Werkzeug läuft. Langsame eingebaute Werkzeuge (Drucker-Snapshots, Suche in Mail und Notizen, Nachrichten, Kalender, Medien, Lesezugriffe auf Infrastruktur und GitHub) tragen pro Sprache einen kurzen Satz, etwa „Ich schaue mir den Drucker an.“. Will ein Sprach-Turn ein solches Werkzeug ausführen, wurde noch nichts gesprochen und dauert das Werkzeug meist länger als `voice.fillerThresholdMs` (der Median seiner letzten 20 Aufrufe), spricht Marshal diesen Satz zuerst. Höchstens ein Satz pro Turn; Text-Turns bekommen nie einen.

## Der Guard als zweite Linie

Der Guard prüft jeden Aufruf noch einmal, in dieser Reihenfolge: Das Werkzeug ist bekannt und wurde in dieser Runde angeboten; es ist noch sichtbar; die Argumente sind gültig; Risikoregeln, Anhebung durch Belastung, Subagenten- und Satellitenrichtlinie. Jede Stufe kann die Risikostufe nur anheben, nie senken.

Die zweite Prüfung zählt, weil sich zwischen dem Bau der Liste und dem Aufruf etwas ändern kann: Eine Regel in `risk.yaml` kann mitten im Turn bearbeitet werden, oder ein Argument wird zu einem Schloss aufgelöst. Ein Aufruf eines Namens, der nicht in der aktuellen Liste steht, wird verweigert, nichts läuft, und das Modell bekommt ein Fehlerergebnis.

## Externe Werkzeuge

MCP-Server und Skills fügen Werkzeuge neben den eingebauten hinzu. Jede Quelle registriert und entfernt sich als Einheit, wenn sich ihre Datei ändert. Ein Durchlauf behält die Werkzeugmenge, mit der er begonnen hat; Änderungen gelten ab dem nächsten Turn. Externe Werkzeuge, die keine Risikoregel trifft, stehen standardmäßig auf `confirm`, ein neuer MCP-Server kann also nichts ohne deine Zustimmung ausführen, bis du Regeln dafür schreibst. Siehe [Eigene MCP-Server](../guides/integrations/custom-mcp-servers.md) und [Risikoregeln](../guides/risk-rules.md).

## Fähigkeiten, die es nicht gibt

Manche Fähigkeiten sind nicht verborgen; sie fehlen. Es gibt kein Werkzeug und keinen Codepfad, um Mail zu senden, Nachrichten zu löschen oder zu verschieben, rohen G-Code zu senden, die Drucker-Firmware neu zu starten, Druckerdateien zu löschen oder eine vom Aufrufer gewählte URL zu laden. Ein fehlendes Werkzeug kann keine Regeländerung, kein Template und kein Prompt einschalten. Siehe [Sicherheitsmodell](security-model.md).

## Abwägungen

- **Eine kürzere Liste macht das Modell manchmal schwächer.** Verbirgst du zu viel, kann das Modell nicht helfen. Allowlists von Templates und Satellitenrichtlinien sind explizit, damit du siehst, was jeder Kontext bekommt.
- **Zwei Stellen setzen dieselben Regeln durch.** Filter und Guard teilen sich eine Implementierung der Prüfungen, laufen aber zweimal pro Aufruf. Die Kosten sind klein neben einem Modellaufruf.

## Verwandte Seiten

::: cards
- [Risiko und Bestätigung](risk-and-confirmation.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [`satellites.yaml`](../configuration/satellites-yaml.md)
- [Format von Templates](../configuration/templates-format.md)
:::
