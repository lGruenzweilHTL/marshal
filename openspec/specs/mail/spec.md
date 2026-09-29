# mail Specification

## Purpose
TBD - created by archiving change integrations. Update Purpose after archive.
## Requirements
### Requirement: Providers and credentials on two axes
Mail access SHALL go through `IMailProvider` (protocol) combined freely with `ICredentialSource` (password / app password, OAuth2 Google, OAuth2 Microsoft). This change SHALL provide the IMAP provider (MailKit) with all three credential strategies (XOAUTH2 for OAuth). Providers SHALL declare capability flags `NativeThreads`, `Labels`, `ServerSearchSyntax`, `PushEvents`, `ScopeEnforcedNoSend`; tools SHALL adapt to them.

#### Scenario: Gmail over IMAP with OAuth
- **WHEN** a Gmail account is added with OAuth2 Google
- **THEN** the IMAP provider authenticates with XOAUTH2 and search works

#### Scenario: Threads without native support
- **WHEN** `mail_read_thread` is called on an IMAP account
- **THEN** the thread is rebuilt from `References` / `In-Reply-To` headers

### Requirement: Mail accounts
Mail accounts SHALL be stored as `MailAccount { owner, alias, provider, credential strategy, credential secret name, settings }` and managed in the accounts view. OAuth accounts SHALL be connected through a server-side OAuth flow started from the view; the refresh token SHALL be stored as an owner DB secret. Aliases (e.g. "private", "work") SHALL be usable as the `account` argument.

#### Scenario: Connect Microsoft account
- **WHEN** the user completes the Microsoft OAuth flow in the accounts view
- **THEN** a `MailAccount` with strategy `oauth_microsoft` exists and its refresh token is a write-only owner secret

### Requirement: Mail tools
The system SHALL provide these safe tools: `mail.search(account?, from, to, subject, text, since, before, unread, folder)` (no account = all accounts), `mail.read_message`, `mail.read_thread`, `mail.list_folders`, `mail.list_labels` (hidden when no account has `Labels`), `mail.create_draft(account, to, subject, body, replyTo?)`, `mail.update_draft(id, ...)`. Tools returning mail content SHALL be `untrustedOutput` and `sensitive`. Draft tools SHALL NOT be escalated by taint.

#### Scenario: Search across accounts
- **WHEN** `mail_search` is called without `account`
- **THEN** results from all of the owner's accounts are returned, each labeled with its alias

#### Scenario: Reading mail taints
- **WHEN** `mail_read_message` returns a message
- **THEN** the run is tainted with source `mail` and the run record stores only the message reference

### Requirement: IMAP drafts
IMAP drafts SHALL be written with `APPEND` to the folder with SPECIAL-USE `\Drafts` (RFC 6154) and the `\Draft` flag. If no folder has `\Drafts`, the account settings SHALL name the drafts folder, otherwise the tool SHALL fail with a clear error.

#### Scenario: German drafts folder
- **WHEN** the server's drafts folder is called "Entwürfe" and marked `\Drafts`
- **THEN** the draft is appended there

### Requirement: No send path
The system SHALL contain no SMTP configuration, no SMTP client and no code that sends mail through any provider. Delete, trash, flag and label changes SHALL NOT exist as tools.

#### Scenario: No send capability
- **WHEN** the tool registry is listed with every mail account configured
- **THEN** no tool can send, delete or move a message

