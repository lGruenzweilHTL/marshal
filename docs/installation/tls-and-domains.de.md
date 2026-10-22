---
icon: fa-solid fa-lock
order: 50
description: "Wähle Hostname und Zertifikat so, dass Browser, Passkeys und Push-Links funktionieren."
---

# TLS und Domains

Wähle Hostname und Zertifikat so, dass Browser, Passkeys und Push-Links funktionieren.

## Warum das wichtig ist

- Browser-Sitzungen brauchen HTTPS ([Reverse Proxy](reverse-proxy.md)).
- Passkeys sind an eine Domain gebunden. Standardmäßig nutzt Marshal den Host der Anfrage. Setz `serverDomain` am Provider `local`, wenn der Proxy den Host-Header nicht prüft:

```yaml
auth:
  providers:
    - name: local
      type: local
      serverDomain: marshal.example.com
```

- Ein OIDC-Provider braucht die Redirect-URI `https://<host>/signin-oidc/<provider name>`. Nimm ein Pfadpräfix mit auf.
- Links in Push-Benachrichtigungen nutzen `notifications.publicUrl`. Displays laden Medien von `displays.mediaBaseUrl`, das ein Fernseher erreichen muss; reines HTTP im LAN ist für sie oft am besten.
- Ein Wechsel der Domain macht bestehende Passkeys ungültig.

## Möglichkeiten

| Aufbau | Zertifikat |
|---|---|
| Öffentliche Domain | Let's Encrypt in Caddy oder Traefik (HTTP- oder DNS-Challenge) |
| Heim-Domain ohne öffentlichen Zugang | DNS-Challenge bei deinem DNS-Anbieter oder ein Wildcard-Zertifikat |
| Nur LAN | Eigene CA des Proxys (Caddy `tls internal`); importiere das Wurzelzertifikat auf deine Geräte |
| VPN-Overlay | Die eigene HTTPS-Funktion des Overlays |

Home Assistant ruft Marshal mit einem API-Token auf und kann deshalb im LAN reines HTTP oder HTTPS nutzen. Mit einer privaten CA muss Home Assistant der Wurzel vertrauen.

## Prüfen, ob es funktioniert

Öffne `https://<host>/` ohne Zertifikatswarnung, melde dich an und füge unter **Sicherheit** einen Passkey hinzu.

## Verwandte Seiten

::: cards
- [`auth`](../configuration/marshal-yaml/auth.md)
- [Lokale Konten](../guides/authentication/local-accounts.md)
:::
