# Get suggestions

Let Marshal notice things at home and suggest what to do, such as closing a window before rain, and teach it which suggestions you want.

## Before you start

- Sign in to the web UI.
- Connect [Home Assistant](integrations/home-assistant.md). The printer suggestions also need [Moonraker printers](integrations/printers-moonraker.md).
- Set your Home Assistant `person` entity in **Preferences** under **Notifications**, so Marshal knows when you are away.
- For push suggestions, set a push target in the same card. See [notifications](notifications.md).

## Steps

::: steps
1. **Turn on "help Marshal learn"**

   Open **Preferences** and switch on **Help Marshal learn**. It is off by default. Without it, no detector runs for you and no suggestion is sent. Turning it off stops new suggestions at once; open ones stay until they expire.

2. **Know the detectors**

   | Detector | Suggests | Proposed action |
   |---|---|---|
   | `window_rain` | An open window or door while rain is likely within the hour. | None. |
   | `energy_anomaly` | A device on the energy dashboard that used at least 1.5 times its usual daily energy yesterday. | None. |
   | `print_done_away` | A print finished while you are away. | Switching the printer off, when `printers[].powerEntity` is set. |
   | `maintenance_counter` | A counter with no [maintenance item](maintenance.md), for example a printer's print hours. | None. The suggestion shows a YAML snippet to copy. |

   Detectors need the integrations they read. A detector without its data stays silent. To change thresholds or turn a detector off for everyone, see [`suggestions`](../configuration/marshal-yaml/suggestions.md).

3. **Answer suggestions**

   Suggestions appear in the **Inbox** under **Suggestions**, after approvals and proposals and before notifications. Each one shows its text and, when it has one, the action **Accept** runs.

   - **Accept** runs the proposed action once, through the guard. When the action is confirm tier, the card says why, and Accept counts as your confirmation.
   - **Dismiss** runs nothing.

   A pushed suggestion has the same **Accept** and **Dismiss** buttons in the notification. Suggestions never use voice and respect your quiet hours. An unanswered suggestion expires after 6 hours, or earlier when its condition no longer holds, for example when you closed the window yourself.

4. **Let Marshal learn what you want**

   Marshal counts your answers per detector and estimates how likely you accept the next one: (accepted + 1) / (accepted + dismissed + 2).

   - The first 3 suggestions of a detector go to the Inbox whatever the estimate.
   - After that, a suggestion with an estimate below 0.3 is dropped.
   - From 0.3 up, it goes to the Inbox with priority `low`.
   - From 0.6 up, it is pushed with priority `normal`.

   Expired suggestions do not change the counts.

5. **Mute or reset a detector**

   Open **Preferences** and find the **Suggestions** card. It lists each detector with its counts and estimate.

   - **Mute for a week** or **Mute** (until you unmute it) stops the detector for you.
   - **Reset counts** clears the counts, so the detector starts exploring again.
:::

## Check that it works

Open a window that has a `binary_sensor` with device class `window` while the forecast shows rain. Within a few minutes the Inbox shows a `window_rain` suggestion.

## Related

- [`suggestions` configuration](../configuration/marshal-yaml/suggestions.md)
- [Turn repeated workflows into skills](skill-proposals.md)
- [Track maintenance by usage](maintenance.md)
- [Risk tiers](../reference/risk-tiers.md)
