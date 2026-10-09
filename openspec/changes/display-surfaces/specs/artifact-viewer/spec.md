# Spec Delta

## MODIFIED Requirements

### Requirement: Artifact viewers
Opening an artifact SHALL show a viewer for its kind:
- `image`, `video`: the browser's native elements;
- `svg`: rendered as an image (never inline in the page's DOM);
- `code`: text highlighted as defined in `code-highlighting`, with line numbers, indent guides and a toolbar showing the language and line count, a wrap toggle (remembered per browser) and a copy button;
- `diff`: the diff view with the code highlighted as defined in `code-highlighting`;
- `model3d`: an interactive 3D view with orbit, zoom and reset (STL and 3MF), loaded only when a 3D artifact is opened;
- `pdf`: opened in the browser's PDF viewer in a new tab;
- `markdown`: rendered with the chat's Markdown rules (HTML disabled, same-origin images only), fenced code blocks highlighted;
- `html`: a sandboxed frame without scripts;
- `other`: name, size and a download button.
Every viewer SHALL offer download, and SHALL show the source and the tainted marker. The highlighter SHALL load only when a code, diff or Markdown artifact is opened.

#### Scenario: 3D model
- **WHEN** the user opens an STL artifact
- **THEN** the model is shown and can be rotated and zoomed

#### Scenario: 3D code loads on demand
- **WHEN** the user opens only image artifacts
- **THEN** the 3D viewer's code is not downloaded

#### Scenario: Markdown with an external image
- **WHEN** a Markdown artifact contains `![x](https://evil.example/x.png)`
- **THEN** no request to `evil.example` is made and the URL is shown as a link

#### Scenario: Copy code
- **WHEN** the user clicks copy in the code viewer
- **THEN** the artifact's full text is on the clipboard

#### Scenario: Wrap toggle remembered
- **WHEN** the user turns wrapping on and opens another code artifact
- **THEN** the second artifact is shown wrapped

#### Scenario: Highlighted fenced code
- **WHEN** a Markdown artifact contains a ```` ```ts ```` fence with `const x = 1`
- **THEN** `const` is shown in the keyword color

## ADDED Requirements

### Requirement: Line links
In the code viewer, clicking a line number SHALL select that line and set the address to `#L<n>`; shift-clicking SHALL extend the selection to a range `#L<a>-L<b>`. Opening an artifact address with such a fragment SHALL highlight the lines and scroll the first one into view. A fragment outside the file SHALL be ignored.

#### Scenario: Link to a range
- **WHEN** the user opens `/artifacts/<id>#L12-L20` for a 100-line code artifact
- **THEN** lines 12 to 20 are highlighted and line 12 is scrolled into view

#### Scenario: Select by clicking
- **WHEN** the user clicks line number 5 and shift-clicks line number 9
- **THEN** the address ends in `#L5-L9` and lines 5 to 9 are highlighted
