---
icon: fa-solid fa-server
description: "Gib Marshal Lesezugriff auf deinen Proxmox-VE-Cluster, damit es über Nodes, Gäste, Speicher und Tasks berichtet, mit einem Token, das nichts ändern kann."
---

# Einen Proxmox-VE-Cluster beobachten

Gib Marshal Lesezugriff auf deinen Proxmox-VE-Cluster, damit es über Nodes, Gäste, Speicher und Tasks berichtet, mit einem Token, das nichts ändern kann.

## Bevor du anfängst

- Proxmox VE mit einer API, die aus dem Container von Marshal über `https` erreichbar ist, meist auf Port 8006.
- Die Berechtigung, in Proxmox Benutzer und API-Tokens anzulegen.
- Für ein selbst signiertes Zertifikat des Nodes: den SHA-256-Fingerabdruck des Zertifikats.

## Schritte

::: steps
1. **Erstelle in Proxmox ein API-Token nur zum Lesen**

   1. Leg unter **Datacenter > Permissions > Users** einen Benutzer für Marshal an, zum Beispiel `marshal@pve`.
   2. Füg unter **Datacenter > Permissions > API Tokens** für diesen Benutzer ein Token namens `marshal` hinzu. Lass **Privilege Separation** an. Kopiere den Wert des Tokens (eine UUID), wenn Proxmox ihn zeigt; er erscheint nur einmal.
   3. Füg unter **Datacenter > Permissions** eine API-Token-Berechtigung hinzu: Pfad `/`, Token `marshal@pve!marshal`, Rolle `PVEAuditor`, Propagate an.

   `PVEAuditor` erlaubt nur Lesen. Marshal schickt außerdem nur `GET`-Anfragen, ein Fehler auf einer Seite öffnet also keinen Schreibweg.

2. **Speichere den Wert des Tokens als Secret**

   Leg ein Secret an, zum Beispiel `proxmox_token`, das die UUID enthält. Siehe [Secrets](../secrets.md). Schreib den Wert nie in `marshal.yaml`.

3. **Pinne das Zertifikat (selbst signierte Nodes)**

   Proxmox-Nodes nutzen standardmäßig ein selbst signiertes Zertifikat. Kopiere seinen SHA-256-Fingerabdruck aus **Node > System > Certificates** oder lies ihn so aus:

   ```sh
   openssl s_client -connect pve.lan:8006 </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha256
   ```

   Mit gesetztem Fingerabdruck nimmt Marshal das Serverzertifikat nur an, wenn sein Fingerabdruck passt, egal wie seine Kette aussieht. Ohne Fingerabdruck wendet Marshal die normale Zertifikatsprüfung an, die bei einem selbst signierten Zertifikat scheitert.

4. **Füg den Abschnitt `proxmox` hinzu**

   ```yaml
   proxmox:
     url: https://pve.lan:8006/
     tokenId: marshal@pve!marshal
     tokenSecret: proxmox_token
     certificateFingerprint: "AB:CD:..."   # 64 Hex-Ziffern, Doppelpunkte optional; bei einem CA-signierten Zertifikat weglassen
   ```

   `url` ist die API-URL eines Nodes des Clusters. Der Start schlägt mit diesen Meldungen fehl, wenn eine Regel verletzt ist:

   - `proxmox.url must be an absolute https URL.`
   - `proxmox.tokenId must look like user@realm!name.`
   - `proxmox.tokenSecret must name a secret.`
   - `proxmox.certificateFingerprint must be a SHA-256 fingerprint (64 hex digits, colons allowed).`

   Ohne Abschnitt `proxmox` registriert Marshal kein Werkzeug `infra_*`. Starte Marshal nach dem Bearbeiten der Datei neu.
:::

## Werkzeuge

Alle Werkzeuge sind in der Stufe safe und scheitern mit `infra.unavailable`, wenn die API nicht erreichbar ist oder das Token ablehnt.

| Werkzeug | Was es tut |
|---|---|
| `infra_overview()` | Pro Node: Name, Online-Zustand, CPU- und Speichernutzung, Laufzeit. Zahl der Gäste nach Status. Speicher mit mehr als 80 % Belegung. |
| `infra_guests(node?, status?)` | VMs und Container mit ID, Name, Typ, Node, Status, CPU, Speicher und Laufzeit. `status` ist `running`, `stopped` oder `paused`. |
| `infra_storage()` | Pro Speicher und Node: Name, Typ, belegt, gesamt und Prozent belegt. |
| `infra_tasks(failedOnly?, since?)` | Bis zu 50 neueste Tasks des Clusters mit Typ, Gast-ID, Node, Benutzer, Start- und Endzeit und Status. |

## Grenzen

- Marshal schickt nur `GET`-Anfragen. Es kann keine Gäste starten, stoppen, anlegen, ändern, migrieren oder löschen, keine Konsolen oder Shells öffnen und keine Cluster-Konfiguration ändern.
- Die Ausgabe enthält keine Notizen oder Beschreibungen von Gästen, die Werkzeuge sind also vertrauenswürdig und belasten keinen Durchlauf.
- Ein abgelehntes Token (HTTP 401) lässt den Aufruf mit einer Meldung scheitern, die sagt, dass das Token abgelehnt wurde. Ein falscher Fingerabdruck scheitert mit `infra.unavailable`, und keine Anfragedaten werden gesendet.
- Marshal liest das Token-Secret bei jeder Anfrage, du kannst es also ohne Neustart wechseln.

## Prüfen, ob es funktioniert

1. Frag „Wie geht es dem Proxmox-Cluster?“ Die Antwort listet deine Nodes mit CPU, Speicher und Laufzeit.
2. Frag „Welche Gäste laufen?“ und „Ist ein Backup fehlgeschlagen?“ Der zweite Aufruf nutzt `infra_tasks` mit `failedOnly`.
3. Scheitert ein Aufruf, sagt dir die Meldung, ob die API nicht erreichbar war, das Token abgelehnt wurde oder das Zertifikat nicht gepasst hat. Siehe [Fehlerbehebung](../troubleshooting.md).

Um eine Nachricht zu bekommen, wenn ein Backup fehlschlägt, statt zu fragen, richte Benachrichtigungen in Proxmox ein, wie unter [Webhooks von Uptime Kuma und Proxmox](uptime-kuma.md#webhooks-von-proxmox-ve) beschrieben.

## Verwandte Seiten

::: cards
- [`proxmox` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Secrets](../secrets.md)
- [Werkzeug-Referenz](../../reference/tools.md)
- [Sicherheitsmodell](../../concepts/security-model.md)
:::
