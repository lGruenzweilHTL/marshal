# Documentation style

How to write and maintain the pages in `docs/`. Code style lives in [STYLE_GUIDE.md](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md).

## Source of truth

The specifications in `openspec/specs/` define how Marshal behaves. Documentation describes that behavior for people; it never invents behavior the specs do not have.

- Write from the spec, then check the code. If they disagree, the spec wins: document the spec's behavior and report the mismatch so the code (or the spec) gets fixed.
- Configuration defaults, key names and validation messages come from the code's options classes and validators, cross-checked against the spec and `deploy/config.example/`.
- Do not document internals that a user or contributor cannot observe or act on.

## Layout

The docs follow [Diátaxis](https://diataxis.fr/). Each page has one job:

| Folder | Kind | Answers |
|---|---|---|
| `getting-started/` | Tutorial | "Walk me through it once." One path, no choices, ends with something working. |
| `installation/`, `guides/`, `home-assistant-component/` | How-to | "How do I do X?" Starts from a goal, lists prerequisites, ends when the goal is reached. |
| `configuration/`, `reference/`, `api/` | Reference | "What exactly does X do?" Complete, terse, structured, no narrative. |
| `concepts/` | Explanation | "Why does it work this way?" Background, trade-offs, design rules. |
| `development/` | Contributor how-to and reference | "How do I change Marshal?" |

Do not mix kinds on one page. A guide links to the reference for the full key list instead of repeating it; a reference page links to the guide for the walkthrough.

## Voice

- Write in English, in plain, direct prose. Address the reader as "you".
- Active voice, present tense: "Marshal hides the tool", not "the tool will be hidden".
- One idea per sentence. Keep most sentences under 25 words.
- Use one term for one thing, as defined in the [glossary](../reference/glossary.md). Do not alternate "confirm tier" and "confirmation level".
- Instructions are imperative: "Set `agent.maxSteps`", not "`agent.maxSteps` should be set".
- No marketing language, no filler ("simply", "just", "easily"), no emoji in body text.
- Say what is not supported plainly, and why if the reason matters (for example, why mail has no send path).

## Page structure

Every page starts with a level-1 heading and a one or two sentence summary of what the page covers. Then:

**Guides** (how-to):

```markdown
# Connect a Moonraker printer

One sentence on the outcome.

## Before you start
- prerequisites, versions, what you need at hand

## Steps
### 1. ...
### 2. ...

## Check that it works

## Troubleshooting   (optional, short; link to guides/troubleshooting.md for more)

## Related
- links to configuration and concept pages
```

**Configuration reference** pages describe one file or one top-level section:

```markdown
# `agent`

One sentence on what the section controls.

## Example
```yaml
...minimal, valid example...
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `agent.maxSteps` | integer | `8` | ... |

## Validation
- what startup rejects, with the exact error text where useful

## Related
```

Durations use the .NET `TimeSpan` format as in the example config (`"00:00:08"`, `"30.00:00:00"`); say so in the type column (`duration`).

**Concept** pages may use any structure, but open with the problem the design solves.

## Markdown conventions

The pages are written in [Clay Flavored Markdown](https://clay-doc.github.io/clay-example-repo/writing/markdown.html) (CFM): GitHub-flavored Markdown plus alerts and components. [Clay](https://clay-doc.github.io/clay-example-repo/) builds them into the documentation site (see [the site](#the-site)). Components stay readable as plain text, so every page still reads well on GitHub.

- Headings: one `#` per page, then `##` and `###`. Do not skip levels. Sentence case ("Before you start").
- Links between pages are relative and include the `.md` extension: `[risk tiers](../reference/risk-tiers.md)`. Link to a heading with `#anchor`. Link to files outside `docs/` (`CONTRIBUTING.md`, `STYLE_GUIDE.md`) by their full GitHub URL; the site does not contain them.
- Callouts use GitHub alerts, sparingly:
  - `> [!NOTE]` extra context
  - `> [!TIP]` a better way
  - `> [!IMPORTANT]` required to succeed
  - `> [!WARNING]` risk of data loss, security impact or a broken setup
- Code blocks always name a language (`yaml`, `sh`, `json`, `http`, `csharp`, `python`, `text`). Name the file in brackets when the block is a file or part of one: ```` ```yaml [/config/marshal.yaml] ````.
- Shell examples use `sh` and no prompt character. Replace values the reader must change with `<angle-brackets>`.
- YAML examples must be valid and must match the real schema. Never put a secret value in a YAML example; reference a secret name, as `/config` requires.
- Tables for keys, flags, events and tools. Prose for everything else.
- Images go in `docs/images/` and need meaningful alt text. Diagrams are PlantUML in `docs/diagrams/` (see [maintaining the diagrams](../diagrams/README.md)); embed the rendered SVG.
- File names: lowercase, hyphen-separated, `.md`.

### Components

Use a component where it fits the content, not for decoration:

| Content | Component |
|---|---|
| The numbered steps of a guide (`## Steps`) | `::: steps`, each step starting with what to do in bold |
| Alternatives the reader picks one of (option A or B, container or source tree) | `::: tabs` with `== Label` lines |
| The same example in several languages or tools (curl and Python) | `::: code-group` with the label in brackets after each language |
| FAQ entries, long logs, edge cases | `::: details <question>` |
| "Where to go next" on start and section pages | `::: cards` |
| A call to an API v1 operation | `::: request v1 <operationId>`, with start values in YAML (`body:`) |

Nest a container inside another by giving the outer one more colons (`:::: tabs` … `::::`).

A step is not a heading, so it has no anchor. When other pages link to a part of a guide, make that part a `###` section instead of a step.

### Front matter

Front matter is optional. Use it for:

- `order` where the sidebar order matters (tutorials, installation), in steps of ten,
- `icon` (a Font Awesome class such as `fa-solid fa-key`) on folders and on pages readers look for,
- the folder's `index.md`: `title`, `icon`, `order`, `description` and `list: true`.

## The site

`clay.yaml` at the repository root configures the site; the [Deploy Docs workflow](https://github.com/lGruenzweilHTL/marshal/blob/master/.github/workflows/docs.yaml) builds it with `npx clay-doc@5 build --strict` and publishes it on GitHub Pages when `docs/`, `clay.yaml` or the OpenAPI document change on `master`.

Before you push, check the docs. It takes a few seconds and catches broken links, anchors and request cells:

```sh
npx clay-doc@5 check
```

Preview the site with live reload at `http://localhost:3000`:

```sh
npx clay-doc@5 dev
```

The [API v1 reference](../api/v1/index.md) is generated: `scripts/docs-openapi.mjs` runs before every Clay command, copies `web/openapi/v1.json` to `artifacts/openapi/`, and adds servers and the authentication schemes. Do not edit the generated pages; change the API and run `npm run gen:api` in `web/`.

## Keeping docs current

- A change that adds, removes or renames a configuration key, tool, event, CLI command, endpoint or integration updates the matching reference page in the same commit.
- A new integration gets a page in `guides/integrations/` and rows in `reference/tools.md`, following [adding an integration](adding-an-integration.md).
- When you notice a page that disagrees with the spec, fix the page, or fix the code if the code is wrong.
