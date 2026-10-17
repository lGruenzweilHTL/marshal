## MODIFIED Requirements

### Requirement: Actionable push
Push notifications for approvals SHALL carry Approve / Reject actions, and push notifications for suggestions SHALL carry Accept / Dismiss actions, through the HA companion app. The reply SHALL arrive as a `mobile_app_notification_action` event and SHALL be accepted only with a valid, unexpired nonce.

#### Scenario: Wrong nonce
- **WHEN** a push action arrives with an unknown nonce
- **THEN** nothing is approved and the event is logged

#### Scenario: Accept a suggestion by push
- **WHEN** an Accept action with the suggestion's valid nonce arrives
- **THEN** the suggestion is accepted and its proposed action, if any, runs once
