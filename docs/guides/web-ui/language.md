---
icon: fa-solid fa-language
description: "Set the interface language and time zone for your account, and the color scheme for one device."
---

# Change language, time zone and appearance

Set the interface language and time zone for your account, and the color scheme for one device.

## Before you start

- Sign in to the web UI.

## Steps

::: steps
1. **Open Preferences**

   Open **Settings > Preferences**.

2. **Language and region**

   - **Language:** German (`de`) or English (`en`). The interface switches without a page reload.
   - **Time zone:** an IANA id such as `Europe/Vienna`. Dates, times and schedules use it. An invalid id such as `Mars/Base` is rejected.

3. **Appearance**

   Choose **System**, **Light** or **Dark**. System follows your operating system. This setting is stored in the browser on this device only, applies immediately and is never sent to Marshal. Another browser keeps its own choice.
:::

## What changes

- All interface text exists in German and English. Dates, times, numbers and durations follow the language and your time zone.
- Content written by you or the model (memory chunks, chat messages, notifications) stays as stored and is not translated.
- Runs that arrive without a request language use your language preference.
- Before you sign in, the login page follows your browser language and falls back to English.

## Defaults

- Language starts as the `owner.language` in `marshal.yaml` for the instance owner. See [`owner`](../../configuration/marshal-yaml/owner.md).
- A new user's time zone starts as `instance.timeZone` (or Home Assistant's time zone, else UTC). See [`instance`](../../configuration/marshal-yaml/instance.md).

> [!NOTE]
> Preferences hold language, time zone, quiet hours, notification targets and do-not-disturb. Softer wishes, such as briefing topics, belong in memory: tell Marshal. See [memory](../memory.md).

## Related

::: cards
- [Notifications](../notifications.md)
  For quiet hours
- [Tour of the web UI](tour.md)
:::
