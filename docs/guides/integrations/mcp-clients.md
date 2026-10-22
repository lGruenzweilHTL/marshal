---
icon: fa-solid fa-laptop-code
description: "Give an external AI client access to what Marshal remembers, your notes and Marshal's tasks, and let it ask Marshal to act, through Marshal's own MCP server at /mcp."
---

# Connect Claude Code or VS Code to Marshal

Give an external AI client access to what Marshal remembers, your notes and Marshal's tasks, and let it ask Marshal to act, through Marshal's own MCP server at `/mcp`.

## Before you start

- Marshal is reachable from the machine that runs the client, for example `https://marshal.example.org`.
- You can sign in to the web UI, or you have shell access to the Marshal host for the CLI.
- The client supports MCP over streamable HTTP with a bearer token: Claude Code, or VS Code with GitHub Copilot agent mode.
- Clients that require OAuth (for example claude.ai or ChatGPT connectors) cannot connect. Marshal's MCP server accepts machine tokens only.

## What the client sees

The tools the client gets depend on the token's scopes:

| Scope | MCP tools | What they do |
|---|---|---|
| `mcp:read` | `memory_recall`, `notes_search`, `notes_read`, `tasks_list`, `tasks_status` | Recall Marshal's memory, search and read your notes, list tasks and read their status. |
| `mcp:write` | the read tools, plus `tasks_create` and `ask_marshal` | Also create background tasks, and ask Marshal to answer or act with its own tools. |

A tool of an integration that is not configured is not listed. For example, the notes tools appear only when [notes](notes.md) are configured. Home control, mail and printers are never exposed as tools. The client reaches them only through `ask_marshal`, so Marshal's guard and risk rules decide every call.

## Steps

::: steps
1. **Create a token**

   In the web UI, open **Security** and create a token named after the client, for example `claude-code`. Select `mcp:read`, and also `mcp:write` if the client may create tasks and ask Marshal to act. Copy the token; it is shown only once.

   Or, on the Marshal host:

   ```sh
   docker compose exec marshal marshal tokens create --name claude-code --scope mcp:read --scope mcp:write
   ```

2. **Add Marshal to the client**

   **Claude Code.** Add the server for your user:

   ```sh
   claude mcp add --transport http --scope user marshal https://marshal.example.org/mcp \
     --header "Authorization: Bearer mrs_..."
   ```

   **VS Code.** Add the server to `.vscode/mcp.json` in your workspace, or to your user MCP configuration. VS Code asks for the token once and stores it securely:

   ```json
   {
     "inputs": [
       { "type": "promptString", "id": "marshal-token", "description": "Marshal API token", "password": true }
     ],
     "servers": {
       "marshal": {
         "type": "http",
         "url": "https://marshal.example.org/mcp",
         "headers": { "Authorization": "Bearer ${input:marshal-token}" }
       }
     }
   }
   ```

3. **Ask Marshal something**

   Ask the client, for example: "Ask Marshal whether the printer is busy." The client calls `ask_marshal` with your text. Marshal runs one turn with its default template and returns the reply and a `conversationId`. When the client passes the `conversationId` again, Marshal continues the same conversation.
:::

## How Marshal treats MCP calls

- **Every call is a run.** Each call appears under **Runs** with source `api` and trigger `mcp:<tool>`, for example `mcp:ask_marshal`.
- **Nothing is confirmed over MCP.** When `ask_marshal` needs a confirm-tier action, for example starting a print, Marshal puts it in the approval queue and the reply says that it waits for approval. A later "yes" from the client does not confirm it. Approve or reject it in the web UI.
- **Costs count against the daily cap.** MCP runs count against `tasks.dailyCostCap`. When the cap is reached, the tools answer with an error that says so, and Marshal calls no model.
- **Calls start untainted.** `ask_marshal` input counts as your own request, like an API turn. A run becomes tainted once it reads untrusted content, and later memory and task writes in that run need approval.

## Check that it works

In the client, list the MCP tools of `marshal`. A token with both scopes lists seven tools, or five without notes. Call `tasks_list`, then open **Runs** in the web UI and find the run with trigger `mcp:tasks_list`.

## Troubleshooting

| Symptom | Cause |
|---|---|
| HTTP 401 | The token is missing, revoked or expired. |
| HTTP 403 | The token has neither `mcp:read` nor `mcp:write`, or the request came with a browser session instead of a token. |
| `Unknown tool` | The token's scopes do not allow the tool. Create a token with `mcp:write`. |
| No notes tools | Notes are not configured. |

## Privacy and injection

- Memory, notes and task results go to the client, and from there to whatever model the client uses, often a cloud model. Use `mcp:read` only for clients you would show your notes to.
- The external agent may itself have read untrusted content, such as a web page or a repository. An injected agent could create tasks or ask Marshal to act. Confirm-tier actions still need your approval in the approval queue. Grant `mcp:write` only to clients you trust, and revoke the token in **Security** if a client misbehaves.

## Related

::: cards
- [Machine tokens](../authentication/machine-tokens.md)
- [API authentication](../../api/authentication.md)
- [Risk rules](../risk-rules.md)
- [Observability](../observability.md)
- [Custom MCP servers](custom-mcp-servers.md)
  The other direction, Marshal as an MCP client
:::
