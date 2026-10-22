---
icon: fa-solid fa-mobile-screen
description: "Add Marshal to your phone's home screen so it opens like an app, without browser chrome."
---

# Install the web UI as an app

Add Marshal to your phone's home screen so it opens like an app, without browser chrome.

## Before you start

- Marshal is reachable over HTTPS with a certificate your phone trusts ([TLS and domains](../../installation/tls-and-domains.md)). Browsers install web apps and run service workers only on secure origins.
- You can sign in to Marshal in the phone's browser.

## Steps

::: steps
1. **Open Marshal in the browser**

   Open your Marshal address in Chrome (Android) or Safari (iOS) and sign in.

2. **Add it to the home screen**

   - **Android (Chrome):** menu, **Install app** or **Add to Home screen**.
   - **iOS (Safari):** Share, **Add to Home Screen**.

   The app is named Marshal and opens standalone.
:::

## Check that it works

Start the app from the home screen. It opens without an address bar and shows Home. Push notification links open in the browser and arrive signed in, because the session cookie is `SameSite=Lax`.

## What the app caches

- The service worker caches only the app shell: scripts, styles, fonts and icons.
- It never caches API responses or the live-event stream.
- Without a connection the app shows a static page, "Marshal is not reachable", with a retry button. Marshal keeps no data on the device, so nothing else is available offline.
- A new Marshal version is picked up the next time you start the app. You do not need to clear caches.

## Troubleshooting

- No install option: the page is not served over HTTPS, or the certificate is not trusted.
- The app shows an old version: close it fully and start it again.
- Sign-in fails over plain HTTP with `auth.https_required`: open Marshal over HTTPS.

## Related

::: cards
- [Tour of the web UI](tour.md)
- [Reverse proxy](../../installation/reverse-proxy.md)
:::
