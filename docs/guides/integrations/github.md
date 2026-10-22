# Read GitHub pull requests, issues and CI runs

Give Marshal read-only access to a short list of GitHub repositories so it can summarize pull requests and issues and explain failed CI runs.

## Before you start

- A GitHub account that can read the repositories you want to list.
- A way to store a secret, see [secrets](../secrets.md).

## Steps

::: steps
1. **Create a fine-grained personal access token**

   In GitHub, open **Settings > Developer settings > Personal access tokens > Fine-grained tokens** and create a token:

   - **Repository access:** select only the repositories you plan to allow.
   - **Permissions**, all set to read-only: Metadata, Contents, Pull requests, Issues, Actions.
   - **Expiration:** GitHub allows at most one year.

   > [!WARNING]
   > Use a separate token for Marshal's coding executor. This token must never have write access.

2. **Store the token as a secret**

   Create a secret such as `github_pat` holding the token. When the token expires, replace the secret value; Marshal reads it on every request and needs no restart.

3. **Add the `github` section**

   ```yaml
   github:
     tokenSecret: github_pat
     repos:
       marshal: lukas/marshal
       homelab: lukas/homelab
   ```

   Each key under `repos` is the id the model sees. It matches `[a-z][a-z0-9-]*`. Each value is `owner/name`. Startup fails with one of these messages if a rule is broken:

   - `github.repos ids must be lowercase letters, digits and dashes, starting with a letter.`
   - `github.repos values must be owner/name.`
   - `github.tokenSecret must name a secret when repos are configured.`

   Without a `github` section, or with an empty `repos`, Marshal registers no `repos_*` tool. Restart Marshal after editing the file.
:::

## Tools

All tools are in the safe tier. Every tool takes a `repo` argument. Its schema is an enum of your repo ids, and the tool description shows each id's `owner/name`.

| Tool | What it does |
|---|---|
| `repos_prs(repo, state?)` | Up to 30 newest pull requests: number, title, author, draft flag, head and base branch, update time. `state` is `open` (default), `closed` or `all`. |
| `repos_pr(repo, number)` | Title, author, state, draft and merged flags, branches, body (cut at 4000 characters), changed files, additions, deletions, and the id of a `diff` artifact. |
| `repos_issues(repo, state?, query?)` | Up to 30 newest issues, without pull requests: number, title, author, labels, comment count, update time. `query` filters titles case-insensitively among the 100 newest. |
| `repos_issue(repo, number)` | Title, author, state, labels, body (cut at 4000 characters) and the 20 newest comments (each cut at 1000 characters). |
| `repos_runs(repo, branch?)` | Up to 20 newest workflow runs: id, workflow, title, branch, event, status, conclusion, short SHA, time. |
| `repos_run_log(repo, runId)` | For each failed job: name, failed step names, the last 50 log lines, and the id of a `code` artifact with the full log. |

A tool fails with `repos.not_found` for an unknown number or run id. It fails with `repos.unavailable` when GitHub cannot be reached or rejects the token, and names the reset time when the rate limit is used up.

### Diffs and logs as artifacts

`repos_pr` stores the pull request diff as a `diff` artifact named like `marshal-pr-42.diff`. `repos_run_log` stores each failed job's full log as a `code` artifact. You can open them in the web UI or show them on a display, see [artifacts](../artifacts.md) and [displays](../displays.md).

- Artifacts belong to the run's owner, have source `github` and are marked tainted.
- A repeated call for the same pull request commit or job returns the existing artifact id instead of storing a new one. A new commit on the pull request stores a new diff.
- If the owner's quota or the size limit is exceeded, the tool still returns its summary and says that the diff or log was not stored.

## Limits

- Marshal sends `GET` requests only. It cannot comment, review, merge, close, label, edit, dispatch or rerun workflows, or change repository settings.
- A repo outside the allowlist fails with `repos.invalid_arguments` and no request reaches GitHub. Requests go only to the configured `owner/name` of the given id.
- Everything the tools return is third-party text. A run that calls any of them is tainted with source `github`. See [untrusted content](../../concepts/untrusted-content.md).
- Tool results are cut as listed above, so very long bodies and comments are shortened.

## Check that it works

1. Ask "What pull requests are open in marshal?" The answer lists open pull requests.
2. Ask "Show me the diff of PR 42". The answer summarizes the PR and links a `diff` artifact; open it in the artifact viewer.
3. Ask "Why did the last CI run fail?" The answer names the failed job, its failed steps and the last log lines.
4. Ask about a repository that is not in your list. Marshal does not see it and cannot read it.

If every call fails with `repos.unavailable`, check that the token has not expired and that it covers the repository.

## Related

- [`github` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Artifacts](../artifacts.md)
- [Secrets](../secrets.md)
- [Executor host](../../installation/executor-host.md)
- [Tool reference](../../reference/tools.md)
