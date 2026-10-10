# Spec Delta

## MODIFIED Requirements

### Requirement: Artifact tools
The system SHALL provide these artifact tools:
- `artifacts.list(kind?, source?, query?)` (safe): at most 50 newest matches with id, name, kind, source, size and creation time. Its output is untrusted, because names can come from mail or executor output;
- `artifacts.show(id, page?)` (safe);
- `artifacts.inspect(id, question, page?)` (safe).

`artifacts.show` SHALL be visible in web turns and in turns from a satellite whose profile has a display, and hidden in all other turns. In a web turn it SHALL emit an `artifact` event on the turn stream with the artifact's id, name, kind and content URL. In a satellite turn it SHALL play the artifact's media rendition (page `page`, default 1) on the satellite's display and return the display name and the page count; the model SHALL NOT be able to choose another display. It SHALL fail with not found for an unknown or foreign id, with `artifacts.no_rendition` naming the kinds the display can show when the artifact has no rendition, and with `artifacts.invalid_arguments` for a page outside the rendition.

`artifacts.inspect` SHALL send page `page` (default 1) of the artifact's media rendition, as an image, with the question to a model with the `vision` capability of the run's tier, falling back to `standard` + `vision`, and return the model's answer and the page count. Its output SHALL be untrusted. It SHALL be hidden when neither tier has a vision model. It SHALL fail with not found for an unknown or foreign id, with `artifacts.no_rendition` for artifacts whose rendition is not an image (no rendition, or video), and with `artifacts.invalid_arguments` for a page outside the rendition.

#### Scenario: Show in a web turn
- **WHEN** a web chat turn calls `artifacts_show` for a 3D model
- **THEN** the turn stream carries an `artifact` event for it and the chat shows a card that opens the 3D viewer

#### Scenario: Voice turn
- **WHEN** a voice turn's tool list is built for a satellite whose profile has no display
- **THEN** `artifacts_show` is not in it and `artifacts_list` is

#### Scenario: Voice turn with display
- **WHEN** a turn from `assist_satellite.living_room` (profile `display: living_tv`) calls `artifacts_show` for an image
- **THEN** the image plays on `living_tv` and the result names `living_tv` and 1 page

#### Scenario: Code on the TV
- **WHEN** a satellite turn calls `artifacts_show` with `page: 2` for a 130-line code artifact
- **THEN** page 2 of 4 plays on the satellite's display

#### Scenario: 3D model on a TV
- **WHEN** a satellite turn calls `artifacts_show` for a `model3d` artifact
- **THEN** page 1 of 2, the shaded view, plays on the satellite's display

#### Scenario: Listing taints
- **WHEN** a run calls `artifacts_list`
- **THEN** the run is tainted with source `artifact`

#### Scenario: Inspect a model
- **WHEN** a run calls `artifacts_inspect` for a `model3d` artifact with `page: 2` and the question "Are the mounting holes on both sides?"
- **THEN** the four-view sheet and the question are sent to the vision model, the answer is returned, and the run is tainted with source `artifact`

#### Scenario: No vision model
- **WHEN** neither the run's tier nor `standard` has a model with `vision`
- **THEN** `artifacts_inspect` is not in the tool list

#### Scenario: Video cannot be inspected
- **WHEN** `artifacts_inspect` is called for an MP4 artifact
- **THEN** the tool fails with `artifacts.no_rendition` and no model is called

## ADDED Requirements

### Requirement: Artifact previews
The system SHALL serve a preview image of an artifact at `GET /api/v1/artifacts/{id}/preview`, with the same sign-in rules as its content. For `model3d` it SHALL be the shaded view of rendition page 1. For `image` it SHALL be the image (the first frame for GIF and WebP). Either way it SHALL be scaled to fit 480 × 270 pixels and served as PNG. For every other kind, and for a model or image that cannot be read, the response SHALL be 404. A preview SHALL carry an `ETag` derived from the artifact's SHA-256, and a request with a matching `If-None-Match` SHALL get 304.

#### Scenario: Model preview
- **WHEN** the owner requests the preview of an STL artifact
- **THEN** the response is a PNG of at most 480 × 270 pixels showing the model

#### Scenario: Code has no preview
- **WHEN** the preview of a `code` artifact is requested
- **THEN** the response is 404

#### Scenario: Foreign artifact
- **WHEN** owner A requests the preview of owner B's model
- **THEN** the response is 404

#### Scenario: Unchanged preview
- **WHEN** the preview is requested again with the `ETag` from the first response
- **THEN** the response is 304 without a body
