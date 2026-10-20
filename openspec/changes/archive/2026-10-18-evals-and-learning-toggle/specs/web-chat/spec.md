## ADDED Requirements

### Requirement: Rating replies
Each completed assistant reply in web chat SHALL offer thumbs up and thumbs down for its run; thumbs down SHALL open an optional note field. The current rating SHALL be shown and SHALL be changeable. Replies of earlier conversations SHALL show their stored ratings.

#### Scenario: Thumbs down with note
- **WHEN** the user clicks thumbs down on a reply and enters "wrong light"
- **THEN** the reply's run is rated `down` with that note and the button shows the rating

#### Scenario: Reply still streaming
- **WHEN** a reply is still streaming
- **THEN** no rating buttons are shown for it
