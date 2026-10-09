# Design

## Context

See proposal.md (Why). Current state:

- `artifacts.show` (`ArtifactToolSource.ShowTool`, `Marshal.Artifacts`) is visible only when `context.Source == SessionSource.Web` and returns an `ArtifactRef` that the turn stream turns into an `artifact` event.
- Signed artifact links (`ApiArtifactLinks`, `Marshal.Api`) sign `owner/id/expiry` with Data Protection, put the signature in a query string and make the link absolute with `notifications.publicUrl`. They are built for browsers: short lifetime, no file extension, HTTPS public origin.
- Satellite profiles (`SatelliteProfile`, `ISatelliteContextProvider.GetProfile`, loaded by `YamlSatelliteContextProvider` in `Marshal.Core`) hold `verbosity`, `notes`, `denyTools`, `denyConfirm`.
- `IHomeAssistantClient.CallServiceAsync` already calls HA services over REST.
- Home Assistant's `dlna_dmr` `play_media` resolves the content type through its UPnP class map (`image`, `video`, …), turns relative URLs into absolute ones and builds DIDL metadata itself. HA's docs recommend plain HTTP and IP addresses; Samsung TVs are "picky about the source". The renderer fetches the URL itself, so Marshal must be reachable from the TV.
- SkiaSharp (with `NativeAssets.Linux.NoDependencies`, no fontconfig) is used by `Marshal.Integrations.Moonraker` for snapshot resizing.

## Goals / Non-Goals

**Goals:**
- One tool for "show it": the turn's client decides where it appears.
- Every link a TV gets works without sign-in, survives a paused video, and cannot be reused for another artifact or page.
- No new network trust: the rendition endpoint serves only what a valid signature names.

**Non-Goals:**
- Caching renditions on disk. Text pages render in a few milliseconds; images are converted on request.
- Discovering displays from Home Assistant. Displays are configured.

## Decisions

### 1. Interfaces

| Interface | Project | v1 implementation | Purpose |
|---|---|---|---|
| `IArtifactRenditions` | Abstractions (Artifacts) | `SkiaArtifactRenditions` (Marshal.Artifacts) | `Describe(record)` → page count, media type, extension, or `artifacts.no_rendition`; `OpenAsync(id, page)` → stream of the rendition |
| `IDisplayLinks` | Abstractions (Artifacts) | `ApiDisplayLinks` (Marshal.Api) | absolute signed display link for owner + artifact + page; verify |
| `IDisplayPlayer` | Abstractions (Displays) | `HomeAssistantDisplayPlayer` (HA integration) | `PlayAsync(entity, url, contentType)` through `media_player.play_media` |
| `IDisplayDirectory` | Abstractions (Displays) | `ConfiguredDisplayDirectory` (Marshal.Core) | display name → entity, and the satellite's display (from the satellite profile) |

`SatelliteProfile` gains `string? Display`. The display directory reads it through `ISatelliteContextProvider.GetProfile` and the `displays` options, so `artifacts.show` needs no knowledge of YAML.

Alternative: one `IDisplaySurface` per display that does everything (render + link + play). Rejected: rendering and linking are independent of where the bytes go, and a later browser display (`kind: web`) would reuse the links but not the renditions.

### 2. Tool: client-aware `artifacts.show`

| Tool | Tier | Visible when |
|---|---|---|
| `artifacts.show(id, page?)` | safe | web turn; or a turn with `SatelliteId` whose profile names a display, and Home Assistant is configured |

Hidden in background runs (no satellite), on satellites without a display, and without HA. `artifacts.list` is unchanged.

Flow in a satellite turn:

```
 artifacts_show(id, page)
   │ store.GetAsync(id)                     → not found
   │ renditions.Describe(record)            → artifacts.no_rendition (lists showable kinds)
   │ page in 1..pages                       → artifacts.invalid_arguments
   │ displays.ForSatellite(satelliteId)     → display (name, entity)
   │ displayLinks.Create(id, page, ext)
   └ player.PlayAsync(entity, link, image|video)
                                            → "Showing <name> on living_tv (page 2 of 3)."
```

Safe tier: showing on the screen of the room the user is speaking in has no effect outside that room, and the model cannot choose another display (no argument for it). A satellite's `denyTools: [artifacts.show]` hides it as usual.

Alternative: separate `displays.show` tool. Rejected: two tools with the same meaning make the model choose by client, which it cannot see reliably; one tool follows "visible where usable".

### 3. Display links

Path form, signature in the path, extension last:

```
{mediaBaseUrl}{pathBase}/api/v1/artifacts/{id}/display/{page}/{signature}.{ext}
```

- Signature: Data Protection protector `Marshal.DisplayLinks` over `owner/id/page/expiry`, base64url (path-safe). Separate purpose string from browser links, so one cannot be used as the other.
- Lifetime `displays.linkMinutes` (default 240, range 5–1440).
- The endpoint is anonymous, verifies the signature (404 on any failure, including a deleted artifact), loads the record through an owner-scoped query for the signature's owner, and streams the rendition with `Results.Stream`/`File` with range processing enabled. `HEAD` is mapped too (DLNA renderers probe).
- Base URL: `displays.mediaBaseUrl`, else `notifications.publicUrl`. Marshal must listen on that address; for LAN HTTP this is a deployment matter (`ASPNETCORE_URLS` / compose port), documented in the example config.

Alternative: reuse `SignedContent` with a longer TTL. Rejected: query strings and missing extensions are what picky renderers fail on, and a 4-hour browser link widens the existing link's exposure.

### 4. Renditions

- `image`: JPEG/PNG streamed from the stored file; GIF/WebP decoded with SkiaSharp (first frame) and encoded as PNG.
- `video`: MP4 streamed as stored. WebM has no rendition (most TVs reject it).
- `code`, `diff`, `markdown`: text decoded as UTF-8 (invalid bytes replaced), tabs to 4 spaces, long lines wrapped at the column that fits 1920 px, split into pages of 40 rows (readable on a TV from the couch); drawn with SkiaSharp at 1920×1080 on a dark background with a header line (`name — page n/m`). Diff rows starting with `+`/`-` (not `+++`/`---`) get a green/red background and a colored marker. Markdown is shown as source text (no rendering). All three are highlighted by `CodeHighlighter` (section 6).
- Font: JetBrains Mono (OFL, already the web UI's monospace font) as an embedded TTF resource in `Marshal.Artifacts`, loaded with `SKTypeface.FromStream`, because the NoDependencies native asset has no fontconfig and the slim container has no fonts.
- Page count for text kinds requires reading and wrapping the file; `Describe` is called once per show and once per page request, which is fine for text artifacts (they are small; files over 2 MB of text are cut at 2 MB with a final "truncated" line).

### 5. Configuration

```yaml
displays:
  mediaBaseUrl: http://192.168.1.10:8080/   # how TVs reach Marshal; https later
  linkMinutes: 240
  surfaces:
    living_tv: { entity: media_player.living_room_tv }
```

```yaml
# satellites.yaml
assist_satellite.living_room:
  display: living_tv
```

Validation at startup (configuration stage): display names, `media_player.` prefix, base URL absolute http(s), base URL present when surfaces exist. Satellite `display` references are validated with `satellites.yaml` (startup and hot reload; a broken profile on reload keeps the previous profiles, like today).

### 6. Code highlighting (C# and TypeScript)

One tokenizer, written twice: `Marshal.Artifacts/Highlighting/` (C#, display pages) and `web/src/artifacts/highlighting/` (TypeScript, web viewer, replacing highlight.js). Both read the same `languages.json` and both test suites run the same `cases.json`, so the two cannot drift silently.

**Why not a library.** TextMateSharp (VS Code grammars) needs the native Oniguruma library and grammar files, and on the web side the matching engine (Shiki) is large; highlight.js has no C# counterpart. Two libraries would color the same file differently on the TV and in the browser. A data-driven heuristic tokenizer is a few hundred lines per side and is enough for reading code.

**Shared files.** `web/src/artifacts/highlighting/languages.json` (definitions) and `cases.json` (parity cases) live in the web tree, because the web build stage copies only `web/`. `Marshal.Artifacts.csproj` embeds `languages.json` through a linked `EmbeddedResource`; the Dockerfile's .NET build stage copies that one file. `Marshal.Artifacts.Tests` links `cases.json` as content.

**Language definition** (fields; all optional except `id`):

| Field | Meaning |
|---|---|
| `name`, `extensions`, `names`, `aliases` | display name (web toolbar), file extensions, exact file names (lower case), fence and `lang` aliases |
| `keywords`, `literals`, `types` | word lists; `ignoreCase` for SQL, PHP, PowerShell, Dockerfile |
| `functionKeywords`, `typeKeywords` | the next name is a `function` / `type` |
| `lineComments`, `blocks` (`open`, `close`, `token`) | comments and multi-line strings |
| `quotes`, `stringPrefixes`, `verbatimPrefixes` | string delimiters; prefixes like `@`/`$` (C#), `f`/`r`/`b` (Python) join the string; verbatim prefixes disable backslash escapes |
| `loose` | quotes and line comments start only after a word boundary or space (YAML `Marshal's`, Bash `${#x}`) |
| `pascalTypes` | capitalized names with a lowercase letter are types |
| `calls` | `name(` is a function (default true) |
| `brackets`, `guides` | bracket pair colors (default true) and indent guides (default true) |
| `metaLines` (`prefix`, `suffix`) | a line starting (and ending) with these is `meta`: `#include`, `#region`, `#[derive]`, `[Fact]` |
| `annotations` | `@Name` is `meta` |
| `variables` | `$name` / `${…}` is a `variable` |
| `keys` | `json`/`yaml`: a string or name before `:` is a `property`; `ini`: a name before `=` is a `property` and `[section]` a `type` |
| `mode` | `xml` (tags, attributes, `<?…?>`, entities, embedded `<script>`/`<style>`), `css` (selectors as `tag`, properties, `#hex` as `number`, `@rules` as `meta`), `markdown`, `gcode` (`G1`/`M104` as `keyword`) |
| `embedded` | xml mode: `script` → `javascript`, `style` → `css`, or the language named by the tag's `lang` attribute (`ts`, `scss`) |

**Tokenizer.** `highlight(language, lines)` returns per line a sorted list of `[start, length, token]`. State carried between lines: open block (comment or string), open XML tag, embedded language with its own state, Markdown fence language with its own state, bracket stack. A pending definition keyword (`class`, `def`) applies to the next name on the same line only. Per position, first match wins: open block close → meta line → block open → line comment → string (with prefix) → variable → annotation → CSS selector / hex color → number → word (CSS selector / key / keyword / literal / pending definition / call / built-in type / PascalCase type) → bracket. A PascalCase name is not a type after `.` or before `(`, `{`, `=` or `;`. Words never cross into another class; numbers start only at a word boundary.

**Indent guides.** `indentGuides(lines)` → unit and guide count per line, computed after tab expansion. Web: a CSS background gradient per line (`--guides`, `--unit` custom properties set from Vue, no inline HTML), so no extra DOM and no style attributes in highlighted HTML. TV: 1 px lines in the gutter color at each guide column on a line's first row.

**Diffs.** `highlightDiff(text)` → rows (`meta`, `hunk`, `added`, `removed`, `context`) with spans offset by one for the marker. Language per file from `+++ b/<path>`, falling back to `diff --git a/x b/<path>`; two states (old side: removed + context, new side: added + context) so a block comment opened in removed lines does not leak into added lines. The inbox's proposal diff view stays unchanged (entry chunk); the artifact viewer gets a lazy `DiffViewer`.

**Markdown.** The tokenizer has a `markdown` mode for display pages. In the web viewer, Markdown is rendered (not shown as source); a lazy `MarkdownViewer` builds a markdown-it renderer with the same rules as chat (shared factory in `chat/markdown.ts`) plus a `highlight` callback using the tokenizer for fenced blocks. Chat keeps its renderer without highlighting so the highlighter stays out of the entry chunk.

**Colors.** `--syntax-<token>` variables in `web/src/ui/tokens.css` for light and dark; the display pages use the dark values from a C# table, and a .NET test parses `tokens.css` and compares.

**Web code viewer.** Toolbar: language name, line count, wrap toggle (`localStorage`, wrapped in try/catch, default off), copy (Clipboard API, success state for 2 s). Lines are table rows with `id="L<n>"`; clicking a number replaces the route hash (`#L5`, shift: `#L5-L9`); selected rows get a highlight background; on load the first selected row scrolls into view.

## Risks / Trade-offs

- [The TV cannot reach Marshal (wrong base URL, firewall)] → HA's call succeeds but nothing appears. The tool cannot detect this. Mitigation: the example config explains `mediaBaseUrl`; the display link is logged at debug level.
- [Samsung renderers refuse some sources] → Extension in the path, `HEAD` support and correct media types address the common causes; verify with the real TV during implementation and note findings in the example config.
- [A display link leaks (e.g. logged by the TV)] → It is bound to one rendition page, expires, and dies with the artifact. Same exposure class as existing signed links, with a longer lifetime that is configurable.
- [Large GIF/WebP decoding cost] → Bounded by `artifacts.maxUploadMb`; only the first frame is decoded.
- [Fixed display per satellite] → "Show it on the office TV" is not possible in v1 (non-goal); the error tells the model where it can show.
