# Executor host

Run the separate executor host so Marshal can start coding sessions with Claude Code in sandbox containers.

## Before you start

- A separate machine, VM or rootless Docker (or Sysbox) daemon. Marshal never gets a Docker socket and never reads the repositories itself.
- Git repositories checked out on that host.
- An Anthropic API key for the sandbox.

## How it works

Marshal calls the host over HTTP with a bearer token. For each session the host creates a git worktree and a branch `marshal/<id>`, and runs `claude -p` in a throwaway container on a Docker network. The host reports back to Marshal with a per-session callback token. A finished session becomes a code proposal you review in the web interface. Merging happens on the host, without pushing. Rejecting deletes the branch. Files written to `.marshal/artifacts/` in the worktree are uploaded as artifacts.

## Steps

### 1. Build the images

```sh
docker build -f src/Marshal.ExecutorHost/Dockerfile -t marshal-executor-host .
docker build -t marshal-claude-sandbox src/Marshal.ExecutorHost/sandbox
```

### 2. Create files on the host

- `executor_host_token`: a long random string. The same value becomes a secret in Marshal.
- `sandbox.env`: the sandbox environment, for example one line `ANTHROPIC_API_KEY=<key>`. Docker passes it to sandboxes; the host never reads it.
- A Docker network `marshal-sandbox` whose egress is limited to what you allow (for example through an allowlist proxy). The repository does not provide the proxy.

### 3. Run the host

```yaml
services:
  executor-host:
    image: marshal-executor-host
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      ExecutorHost__MarshalUrl: http://marshal.lan:8080/
      DOCKER_HOST: <rootless docker socket>
    volumes:
      - /srv/repos:/srv/repos
      - /srv/worktrees:/srv/worktrees
      - ./secrets:/run/secrets:ro
```

The paths of `/repos` and `/worktrees` must be identical on the Docker host and in the container, because sandboxes mount them by path. Set the options to those paths:

| Option (`ExecutorHost__...`) | Default | Description |
|---|---|---|
| `MarshalUrl` | none, required | Marshal's absolute URL as seen from the host |
| `TokenFile` | `/run/secrets/executor_host_token` | File with Marshal's token; re-read on each request |
| `RepositoriesRoot` | `/repos` | Subdirectories are the repositories |
| `WorktreesRoot` | `/worktrees` | Per-session worktrees |
| `Image` | `marshal-claude-sandbox` | Sandbox image |
| `Network` | `marshal-sandbox` | Sandbox network |
| `EnvFile` | `/run/secrets/sandbox.env` | Sandbox environment file |
| `MaxTurns` | `50` | Claude Code turn limit |
| `MaxBudgetUsd` | `5` | Budget per session |

The host answers `GET /health` without authentication.

### 4. Configure Marshal

Store the token as the secret `executor_host_token`, then add to `marshal.yaml`:

```yaml
executors:
  callbackBaseUrl: http://marshal:8080/
  claudeCode:
    url: http://executor-host:8080/
    tokenSecret: executor_host_token
    repositories:
      - name: marshal
        description: this project
```

`name` is a directory below `RepositoriesRoot`. `callbackBaseUrl` is Marshal as the host sees it. The tool `tasks.run_executor` stays hidden until `executors.claudeCode.url` is set. Restart Marshal.

## Check that it works

```sh
curl http://executor-host:8080/health
```

returns `{"status":"ok"}`. Ask Marshal for a small coding task and review the resulting proposal.

## Related
- [Concepts: security model](../concepts/security-model.md)
- [Approvals](../guides/tasks/approvals.md)
