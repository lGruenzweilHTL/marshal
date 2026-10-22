# notifications Specification

## Purpose
How proactive messages reach the user: priorities, channels chosen by presence, quiet hours, noise control, the inbox, history and actionable push.

## Requirements

### Requirement: Priorities
Notifications SHALL have priority `critical`, `normal` or `low`. Only system-defined sources (e.g. the print babysitter, e-stop, printer error) SHALL send `critical`; the agent's `notifications.notify` SHALL accept only `normal` and `low`. `critical` SHALL always be delivered by voice and push, ignoring quiet hours, DND and caps. `low` SHALL never use voice and SHALL go to the inbox (or a push digest).

#### Scenario: Agent tries critical
- **WHEN** the model calls `notifications_notify` with priority `critical`
- **THEN** the call fails validation

#### Scenario: Critical at night
- **WHEN** the babysitter sends `critical` during quiet hours
- **THEN** it is delivered by voice and push

### Requirement: Channels and choice
Channels SHALL be `voice` (announce, or conversation when a reply is expected), `push` and `inbox`, each an `INotificationChannel`. Channel choice SHALL follow: the delivery's channel and conditions; then presence (owner's `person.*` home → voice in the occupied room via occupancy `binary_sensor.*`, away → push); then fallback push. Notifications SHALL target a person, not a device. Open browsers SHALL receive every notification on the live-event stream regardless of channel.

#### Scenario: Away from home
- **WHEN** a `normal` notification with channel "where I am" is sent and `person` is `not_home`
- **THEN** it is delivered by push

#### Scenario: Home in the office
- **WHEN** the owner is home and only `binary_sensor.office_occupancy` is `on`
- **THEN** the voice notification plays on the office satellite

### Requirement: Quiet hours and override
During the owner's quiet hours (weekly schedule, owner's time zone), `normal` SHALL be sent as silent push or held until quiet hours end (configurable), and `low` SHALL go to the inbox. A task or watch with `override_quiet_hours_until` (set only through a confirm-tier call) SHALL bypass quiet hours for its deliveries until that time.

#### Scenario: Held until morning
- **WHEN** a `normal` notification arrives at 23:00 with quiet hours 22:00–07:00 and mode "hold"
- **THEN** it is delivered at 07:00 and recorded as `held` then `delivered`

#### Scenario: Override
- **WHEN** a print-failure watch has an override until 08:00 and fires at 03:00
- **THEN** its notification is delivered by voice

### Requirement: Noise control
The router SHALL coalesce `normal` items within ~2 minutes into one message, limit voice announcements per source (e.g. 1 per task per 30 minutes; push coalesced instead), apply a global voice cap (e.g. 6 unsolicited per hour, beyond → push), and honor voice DND ("quiet for 2 hours"), which `critical` bypasses.

#### Scenario: Coalescing
- **WHEN** three `normal` notifications arrive within one minute
- **THEN** one message is delivered and two are recorded as `coalesced`

#### Scenario: Voice cap
- **WHEN** the seventh unsolicited voice notification in an hour arrives
- **THEN** it is delivered by push

### Requirement: Inbox tools
The system SHALL provide `notifications.notify(message, priority, deliver)` (safe), `notifications.inbox_list` (safe) and `notifications.inbox_consume(ids)` (safe) for the owner's inbox.

#### Scenario: Briefing consumes the inbox
- **WHEN** a run lists the inbox and consumes two items
- **THEN** those items have `consumed_at` set and are not listed again

### Requirement: History
Every notification SHALL be recorded with content, priority, chosen channel, outcome (`pending`, `delivered`, `coalesced`, `held`, `inboxed`, `expired`, `fallback`) and the reason for the decision.

#### Scenario: Why didn't it tell me
- **WHEN** a notification was pushed instead of spoken because of the voice cap
- **THEN** its history entry has outcome `delivered`, channel `push` and reason `voice_cap`

### Requirement: Actionable push
Push notifications for approvals SHALL carry Approve / Reject actions, and push notifications for suggestions SHALL carry Accept / Dismiss actions, through the HA companion app. The reply SHALL arrive as a `mobile_app_notification_action` event and SHALL be accepted only with a valid, unexpired nonce.

#### Scenario: Wrong nonce
- **WHEN** a push action arrives with an unknown nonce
- **THEN** nothing is approved and the event is logged

#### Scenario: Accept a suggestion by push
- **WHEN** an Accept action with the suggestion's valid nonce arrives
- **THEN** the suggestion is accepted and its proposed action, if any, runs once
