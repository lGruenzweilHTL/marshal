---
icon: fa-solid fa-envelope
description: "Give Marshal read access to your mail over IMAP and let it prepare drafts, with no way to send, delete or move anything."
---

# Connect a mail account

Give Marshal read access to your mail over IMAP and let it prepare drafts, with no way to send, delete or move anything.

## Before you start

- An IMAP mailbox: any provider, Gmail, or Microsoft 365 / Outlook.com.
- One of these credentials:
  - **Password or app password.** Create an app password at your provider if it offers them (Gmail with 2-step verification, iCloud, Fastmail and others). Do not use your main password where an app password exists.
  - **Google sign-in** or **Microsoft sign-in** (OAuth2 with XOAUTH2). This needs an OAuth client that the instance admin registers once.
- The IMAP host, port and security (993 with TLS, or 143 with STARTTLS).

## Steps

::: tabs
== Password or app password
1. In the web UI open **Mail accounts**.
2. Under **Add an IMAP account** enter an alias (1 to 32 lowercase letters, digits or `-`, such as `private` or `work`), host, port, security, user name and the password.
3. Optional: set a **Drafts folder**. You only need it when the server marks no folder `\Drafts`.
4. Select **Add account**.

The password becomes a write-only secret of your user. It is stored encrypted and never shown again. Accounts live in the database, not in `marshal.yaml`.
== Google or Microsoft sign-in
1. Register an OAuth client with the provider. Redirect URI: `<public URL>/oauth/callback/google` or `<public URL>/oauth/callback/microsoft`.
   - Google needs the `https://mail.google.com/` scope.
   - Microsoft needs `IMAP.AccessAsUser.All` and `offline_access`.
2. Store each client secret as a secret and name it in `marshal.yaml`:

```yaml [/config/marshal.yaml]
oauth:
  google: { clientId: "<id>.apps.googleusercontent.com", clientCredentialSecret: google_oauth_client }
  microsoft: { clientId: "<application id>", clientCredentialSecret: microsoft_oauth_client }
```

3. Restart Marshal. Without a client the connect button for that provider is disabled.
4. In **Mail accounts** under **Connect with sign-in** enter an alias and your address, then select **Connect Google** or **Connect Microsoft** and sign in.

Marshal keeps only the refresh token, as a write-only secret of your user. The IMAP hosts are `imap.gmail.com` and `outlook.office365.com`, both on port 993 with TLS. Keys are in the [`oauth` reference](../../configuration/marshal-yaml/integrations.md#oauth-mail-account-connections).
:::

### Remove an account

Select **Remove** on the account. Marshal can no longer read it and deletes its stored credential.

## Tools

| Tool | Tier | What it does |
|---|---|---|
| `mail.search(account?, from, to, subject, text, since, before, unread, folder, limit)` | safe | Searches one account, or all of yours when `account` is omitted. Results carry the alias. 1 to 50 results. The folder defaults to the inbox. |
| `mail.read_message(id)` | safe | One message as text. Attachments are listed by name only. |
| `mail.read_thread(id)` | safe | The whole conversation, oldest first. On IMAP the thread is rebuilt from `References` and `In-Reply-To`. |
| `mail.list_folders(account?)` | safe | Folders of an account. |
| `mail.list_labels(account?)` | safe | Labels. Hidden unless an account supports labels. |
| `mail.create_draft(account, to, subject, body, replyTo?)` | safe | Saves a draft. Nothing is sent. |
| `mail.update_draft(id, to, subject, body, replyTo?)` | safe | Replaces a draft that Marshal created. |
| `mail.save_attachment(message, attachment)` | confirm | Saves one attachment as an artifact. `attachment` is its name or position. Hidden while you have no mail account. |

`search`, `read_message` and `read_thread` return third-party text, so their output is untrusted and sensitive. Reading mail taints the run with source `mail`, and the run record stores only the message reference. In a tainted run memory and task writes need confirmation, with a reason that names mail. Draft tools are not escalated by taint, because a draft cannot do anything until you send it yourself.

`mail.save_attachment` always asks for confirmation. It fetches only that attachment and stores it as a tainted artifact (source `mail`). It fails, naming the message's attachments, when the name does not exist. See [artifacts](../artifacts.md).

### Drafts

Marshal writes drafts with IMAP `APPEND` into the folder marked `\Drafts` (RFC 6154), with the `\Draft` flag. A German "Entwürfe" folder works if the server marks it. If no folder is marked and the account names none, the tool fails with a clear error. You open the draft in your mail client, check it and send it yourself.

## Why there is no send path

Marshal reads mail and drafts replies. It cannot send, delete, trash, flag, label or move a message, and this is a design rule, not a missing feature.

- Mail is the main way outsiders put text in front of the model. A message can hide instructions. If the same model could send mail, a single hostile message could make Marshal leak or forge mail in your name.
- Marshal contains no SMTP configuration, no SMTP client, no `messages.send` or `drafts.send` call and no Graph `Mail.Send` scope. Nothing a prompt says can enable what the code does not contain.
- A draft puts a human between the model and the recipient. You review it and press send.
- OAuth sign-in asks for IMAP access, not for sending rights.

The tool list contains no tool that can send, delete or move a message, whatever accounts you add.

## Limits

- IMAP only. Gmail and Microsoft work through IMAP with XOAUTH2.
- Your accounts are private to your user. Every account row has an owner.
- Plain text bodies; drafts are plain text.
- New-mail events come from IMAP IDLE (renewed every 9 minutes) with a 2-minute poll fallback.

## Check that it works

1. **Mail accounts** lists the account with its strategy.
2. Ask "do I have unread mail from the bank?" The model calls `mail_search` and the answer names the account alias.
3. Ask "draft a reply to Anna's last mail saying I'll be there at 6". A draft appears in your Drafts folder. Nothing is sent.

## Troubleshooting

- **Credential missing.** The stored secret is gone. Remove and add the account again.
- **Drafts folder error.** Set the **Drafts folder** on the account.
- **Connect button disabled.** No OAuth client is configured for that provider.
- **"The sign-in expired".** Start the connection again from the same browser.

See [troubleshooting](../troubleshooting.md).

## Related

::: cards
- [Untrusted content](../../concepts/untrusted-content.md)
- [Secrets](../secrets.md)
- [Daily briefing](../tasks/daily-briefing.md)
- [Risk tiers](../../reference/risk-tiers.md)
- [Integration sections reference](../../configuration/marshal-yaml/integrations.md)
:::
