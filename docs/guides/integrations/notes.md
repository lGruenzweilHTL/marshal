---
icon: fa-solid fa-note-sticky
description: "Let Marshal search and read your Markdown notes from a git repository, kept separate from its memory."
---

# Connect your notes

Let Marshal search and read your Markdown notes from a git repository, kept separate from its memory.

## Before you start

- A git repository of Markdown files. Only `.md` files are indexed.
- Either a repository already placed in `/data/notes`, or a remote that Marshal clones there.
- For a private remote: an https access token or an SSH deploy key (private key), stored as a secret.
- A multilingual embedding model, see [models setup](../../installation/models-setup.md). Search is hybrid: full-text and embeddings.

## Steps

::: tabs
== Local repository
Put the repository into `/data/notes` (the `/data` volume). Notes are on when that directory exists. No configuration is needed.
== Remote repository
```yaml
notes:
  remote: https://git.example.com/lukas/notes.git
  branch: main
  credentialSecret: notes_token
  pullInterval: "00:15:00"
```

Marshal clones the repository into `/data/notes` on first start and pulls it every `pullInterval`. It is a **read-only mirror**: Marshal never commits or pushes. Keep `credentialSecret` as a secret name. The value is an https token or an SSH deploy key. Omit it for a public repository. Keys and limits are in the [`notes` reference](../../configuration/marshal-yaml/integrations.md#notes).

Restart Marshal after changing `marshal.yaml`.
:::

## Tools

All notes tools are safe. Their results are untrusted and sensitive, so reading a note taints the run with source `notes`.

| Tool | What it does |
|---|---|
| `notes.list(folder?)` | Lists notes, optionally in one folder (500 at most). |
| `notes.read(path)` | Reads one note. Text is cut at `notes.maxReadCharacters` (default 50000). |
| `notes.search(query, limit?)` | Hybrid search by meaning and keywords. Returns matching sections. `limit` 1 to 20. |

Paths are confined to the repository: absolute paths, `..`, hidden entries such as `.git` and symbolic links are refused, and nothing outside the repository is read. There are no write tools.

## Notes are not memory

Marshal never puts notes into a turn's context by itself. A note reaches the model only when it calls a notes tool. Content enters memory only when the agent writes a chunk in its own words with `source_ref` set to the note path, and a tainted run asks for confirmation before it does. See [memory](../memory.md).

The notes index (path, heading, text, embedding) is derived data. It is not exported or backed up, and Marshal rebuilds it from the repository. After a pull, only the changed files are reindexed.

## Limits

- Markdown files only. Other files are ignored.
- Changes in a remote appear after the next pull (default 15 minutes, at least 1 minute).
- Search quality depends on the embedding model.

## Check that it works

1. Ask "what did I write about the garage project?" The model calls `notes_search`.
2. Ask it to open the best match. It calls `notes_read` with a path such as `projects/garage.md`.
3. For a remote, push a change and wait for `pullInterval`. The new text is searchable.

## Troubleshooting

- **No `notes_` tools.** There is no `remote` and `/data/notes` does not exist.
- **Clone fails.** Check the URL and `credentialSecret`. An SSH key must be a private key.
- **A note is missing from search.** It is not a `.md` file, or the index has not caught up yet.

## Related

::: cards
- [Memory](../memory.md)
- [Untrusted content](../../concepts/untrusted-content.md)
- [Volumes and layout](../../installation/volumes-and-layout.md)
- [Secrets](../secrets.md)
:::
