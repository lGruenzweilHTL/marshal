# Spec Delta

## ADDED Requirements

### Requirement: Seerr source
The `seerr` source SHALL read the JSON body that the user configures as Seerr's webhook payload template: `notification_type` and a `media` object with `media_type`, `tmdbId`, `status` and `jellyfinMediaId`, and optionally a `request` object with `request_id`. It SHALL publish:
- `MEDIA_AVAILABLE` as `media.available`;
- `MEDIA_APPROVED` and `MEDIA_AUTO_APPROVED` as `media.approved`;
- `MEDIA_DECLINED` as `media.declined`;
- `MEDIA_FAILED` as `media.failed`;
- nothing for other notification types (e.g. `MEDIA_PENDING`, `TEST_NOTIFICATION`, issue notifications) and for a body without `media`.
The entity SHALL be `<media_type>:<tmdbId>` (e.g. `movie:438631`). The payload SHALL hold the media type, TMDB id, request id, Jellyfin item id and the Jellyfin link (see `media-requests`), and no other field from the body. The source's events SHALL be trusted. Its scope SHALL be `hooks:seerr`.

#### Scenario: Movie available
- **WHEN** the body has `notification_type: MEDIA_AVAILABLE` and `media: { media_type: movie, tmdbId: "438631", jellyfinMediaId: "abc123" }`
- **THEN** a trusted `media.available` event with entity `movie:438631` and Jellyfin item `abc123` is published

#### Scenario: Test notification
- **WHEN** the body has `notification_type: TEST_NOTIFICATION`
- **THEN** the response is 202 and no event is published

#### Scenario: Free text is dropped
- **WHEN** the body also has `subject`, `message` and `image` keys
- **THEN** the published payload contains none of them

#### Scenario: Unreadable body
- **WHEN** the body is not JSON, or `media` is present but has no `tmdbId`
- **THEN** the response is 400 and nothing is published
