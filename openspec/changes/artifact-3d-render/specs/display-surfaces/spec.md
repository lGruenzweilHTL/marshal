# Spec Delta

## MODIFIED Requirements

### Requirement: Media renditions
Each artifact kind SHALL have at most one media rendition that a media renderer can play:
- `image`: JPEG and PNG as stored; GIF and WebP converted to PNG (first frame);
- `video`: MP4 as stored;
- `code`, `diff`, `markdown`: the text rendered as PNG pages of at most 40 lines of monospace text on a dark background, 1920×1080 pixels, with the artifact name and page number in a header; highlighted as defined in `code-highlighting` with the dark theme's token colors; `code` with indent guides; in `diff`, added rows on a green and removed rows on a red background with a colored marker; lines longer than the page wrap, and a wrapped row keeps its colors but has no indent guides;
- `model3d`: two PNG pages of 1920×1080 pixels on the same dark background, with the artifact name, page number and the model's bounding box size in millimetres in a header. Page 1 is one shaded view from the front-right above. Page 2 is a sheet of four views: front, right, top and that perspective view. The model SHALL be centered and scaled to fill its view. Surfaces SHALL use the material or object colors from a 3MF file when present, else one default color, shaded by their angle to a fixed light. An STL or 3MF file that cannot be read, has no triangles, or has more than 5 million triangles SHALL have no rendition;
- every other kind and WebM video: no rendition.
A rendition SHALL be produced from the stored file only; renditions SHALL NOT be stored as artifacts.

#### Scenario: Code pages
- **WHEN** a 130-line `.cs` artifact is rendered
- **THEN** it has 4 PNG pages, the second one showing lines 41 to 80

#### Scenario: Highlighted code on the TV
- **WHEN** a `.cs` artifact containing `public sealed class Program` is rendered
- **THEN** `public` is drawn in the keyword color and `Program` in the type color

#### Scenario: Diff on the TV
- **WHEN** a diff of `src/App.cs` with the added line `+var x = 1;` is rendered
- **THEN** the row has the added background and `var` is drawn in the keyword color

#### Scenario: 3D model pages
- **WHEN** a binary STL of a 40 × 20 × 10 mm bracket is rendered
- **THEN** it has 2 PNG pages, the header shows `40.0 × 20.0 × 10.0 mm`, and page 2 shows four views

#### Scenario: 3MF colors
- **WHEN** a 3MF file with a red and a blue object is rendered
- **THEN** both objects are drawn in shades of their colors

#### Scenario: No rendition for a 3D model
- **WHEN** the rendition of a file named `part.stl` with no triangles is requested
- **THEN** the result is a failure with code `artifacts.no_rendition`
