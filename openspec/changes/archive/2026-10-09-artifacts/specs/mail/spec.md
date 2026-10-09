## ADDED Requirements

### Requirement: Saving attachments
The system SHALL provide `mail.save_attachment(message, attachment)` (confirm tier, `persists`), where `attachment` is the attachment's name or position as listed by `mail.read_message`. It SHALL fetch only that attachment and store it as a tainted artifact with source `mail` and the message reference, and return the artifact's id, name and kind. It SHALL be hidden while the owner has no mail account.

#### Scenario: Save an invoice
- **WHEN** the user confirms `mail_save_attachment` for attachment `invoice.pdf` of a message
- **THEN** an artifact `invoice.pdf` of kind `pdf`, source `mail`, tainted, exists and the tool result names its id

#### Scenario: Always confirmed
- **WHEN** the model calls `mail_save_attachment` in any run
- **THEN** a pending confirmation is created and nothing is fetched until the user confirms

#### Scenario: Unknown attachment
- **WHEN** the confirmed call names an attachment the message does not have
- **THEN** the call fails naming the message's attachments and nothing is stored
