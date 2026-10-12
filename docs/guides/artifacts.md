# Work with artifacts

Upload, view, share and let Marshal show files such as 3D models, code, diffs, images and documents.

## Before you start

- Sign in to the web UI. Upload, list and delete need a browser session.
- Optional: [displays](displays.md) to show artifacts on a TV, and a model with `vision: true` for `artifacts_inspect`.

## What artifacts are

An artifact is a file Marshal produced or received, stored under `/data/artifacts/<owner>/` with a stable id. Another user's artifact behaves as not found.

| Source | How it arrives | Tainted |
|---|---|---|
| `upload` | Web UI upload | No |
| `printer` | You keep a printer snapshot | No |
| `executor` | A coding session uploads output | Yes |
| `mail` | `mail.save_attachment` | Yes |
| `github` | Diffs and failed-job logs stored by the `repos` tools | Yes |

Tainted artifacts come from third-party content. The list marks them. See [untrusted content](../concepts/untrusted-content.md).

## Steps

### Upload a file

1. Open **Activity > Artifacts**.
2. Choose upload and pick a file (at most `artifacts.maxUploadMb`, default 200 MB). A larger file is rejected with 413.

New artifacts appear in the list at once, without a reload.

### Find and filter

The list shows newest first with name, kind, source, size, creation time and the tainted marker. Filter by kind and source, search by name. Images and 3D models show a thumbnail; other rows show their kind's icon. The quota usage is shown too.

### Open and view

| Kind | Viewer |
|---|---|
| `image`, `video` | Browser's native player. |
| `svg` | Shown as an image, never inline. |
| `code` | Highlighted text with line numbers, indent guides, language and line count, a wrap toggle (remembered per browser) and a copy button. |
| `diff` | Diff view with highlighted code. |
| `model3d` | Interactive 3D view for STL and 3MF: orbit, zoom, reset. Loads only when you open a 3D artifact. |
| `pdf` | Browser's PDF viewer in a new tab. |
| `markdown` | Rendered like chat: HTML off, same-origin images only, fenced code highlighted. |
| `html` | Sandboxed frame without scripts. |
| `other` | Name, size and a download button. |

Every viewer offers download and shows source and tainted marker.

### Link to lines

In the code viewer, click a line number to select it (`#L12`), shift-click to extend (`#L12-L20`). Open an address such as `/artifacts/<id>#L12-L20` to jump to those lines. A fragment outside the file is ignored.

### Delete

Choose delete and confirm in the dialog. The row and the file are removed and its links stop working.

## How kinds are detected

Marshal decides the kind from the file extension and the file's leading bytes, never from a declared media type alone. A file whose content does not match its extension is stored as `other`: a `photo.png` that contains HTML is `other` and downloads instead of rendering.

Kinds: `image` (PNG, JPEG, GIF, WebP), `svg`, `video` (MP4, WebM), `model3d` (STL, 3MF), `code`, `diff` (`.diff`, `.patch`), `pdf`, `markdown`, `html`, `other`.

## Syntax highlighting

The web viewer and the TV pages share one highlighter and one definition file, so a file looks the same in both. Languages: C#, TypeScript, JavaScript, XML (also `.csproj`, `.props`, `.vue`, `.html`, `.svg`), Python, OpenSCAD, YAML, JSON, TOML and INI, Bash, PowerShell, C, C++, Rust, Go, Java, Kotlin, Swift, Ruby, PHP, SQL, CSS, SCSS, G-code, Dockerfile and Markdown. The language comes from the file name; an unknown extension stays plain. Brackets are colored by nesting depth. Diffs take each file's language from its header.

## Safe serving

Content responses carry the detected media type and `X-Content-Type-Options: nosniff`. `html` and `svg` are served with a sandbox that allows no scripts, forms or popups. `other` is always a download.

## Links

The content URL needs your session or a token. Marshal can also issue signed links that need no sign-in, are bound to one artifact and expire after `artifacts.signedLinkMinutes` (default 15), for example for a phone notification. Expired, altered or foreign links get 404.

## Quota

Each user has `artifacts.quotaMb` (default 5120 MB). A write over the quota fails with an error naming the quota and current usage, and stores nothing. Marshal keeps artifacts until you delete them.

```yaml
artifacts:
  quotaMb: 5120
  maxUploadMb: 200
  signedLinkMinutes: 15
```

See [`artifacts`](../configuration/marshal-yaml/artifacts.md).

## What Marshal can do with artifacts

| Tool | Tier | Effect |
|---|---|---|
| `artifacts_list(kind?, source?, query?)` | safe | Up to 50 newest matches. Output is untrusted and taints the run. |
| `artifacts_show(id, page?)` | safe | In a web turn, shows the artifact in chat (images inline, other kinds as a card that opens the viewer). In a satellite turn with a display, plays its rendition on that display. Hidden in other turns. |
| `artifacts_inspect(id, question, page?)` | safe | Sends page `page` of the rendition as an image to a vision model and returns the answer, for example "Are the mounting holes on both sides?" Hidden when neither the run's tier nor `standard` has a vision model. Fails with `artifacts.no_rendition` for video and kinds without an image rendition. |

Output of `artifacts_inspect` is untrusted and taints the run. Errors: not found for unknown or foreign ids, `artifacts.invalid_arguments` for a page outside the rendition.

Chat cards stay with the message when you reopen the session. Previews (`GET /api/v1/artifacts/{id}/preview`) are PNGs up to 480x270 for images and 3D models; other kinds answer 404.

## Troubleshooting

- Upload rejected with 413: the file is over `artifacts.maxUploadMb`.
- Quota error: delete artifacts or raise `artifacts.quotaMb`.
- `artifacts_inspect` missing: no model with `vision: true`. See [models setup](../installation/models-setup.md).
- 3D model without thumbnail: the file cannot be read or has no triangles.

## Related

- [Displays](displays.md)
- [Artifact kinds and tools](../reference/tools.md)
- [Printers](integrations/printers-moonraker.md), [GitHub](integrations/github.md), [mail](integrations/mail.md)
