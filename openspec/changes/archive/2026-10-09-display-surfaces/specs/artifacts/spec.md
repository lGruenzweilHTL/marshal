## MODIFIED Requirements

### Requirement: Artifact tools
The system SHALL provide `artifacts.list(kind?, source?, query?)` (safe; output untrusted, because names can come from mail or executor output; at most 50 newest matches with id, name, kind, source, size and creation time) and `artifacts.show(id, page?)` (safe). `artifacts.show` SHALL be visible in web turns and in turns from a satellite whose profile has a display, and hidden in all other turns. In a web turn it SHALL emit an `artifact` event on the turn stream with the artifact's id, name, kind and content URL. In a satellite turn it SHALL play the artifact's media rendition (page `page`, default 1) on the satellite's display and return the display name and the page count; the model SHALL NOT be able to choose another display. It SHALL fail with not found for an unknown or foreign id, with `artifacts.no_rendition` naming the kinds the display can show when the artifact has no rendition, and with `artifacts.invalid_arguments` for a page outside the rendition.

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
- **THEN** the tool fails with `artifacts.no_rendition` and nothing is sent to Home Assistant

#### Scenario: Listing taints
- **WHEN** a run calls `artifacts_list`
- **THEN** the run is tainted with source `artifact`
