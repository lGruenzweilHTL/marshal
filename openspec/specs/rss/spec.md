# rss Specification

## Purpose
TBD - created by archiving change integrations. Update Purpose after archive.
## Requirements
### Requirement: FreshRSS client
The system SHALL access FreshRSS through its Google Reader-compatible API (`/api/greader.php`) with username and API password (`apiPasswordSecret`), using `continuation` for pagination.

#### Scenario: Pagination
- **WHEN** more unread items exist than one page holds
- **THEN** the client follows `continuation` until the requested limit is reached

### Requirement: RSS tools
The system SHALL provide safe tools `rss.list_feeds`, `rss.list_categories`, `rss.get_unread(category?, since?, limit)`, `rss.get_article(id)`, `rss.search(query)`, `rss.mark_read(ids)` and `rss.fetch_full_text(articleId)`. Article content SHALL be `untrustedOutput`. Subscribe, unsubscribe and delete SHALL NOT exist.

#### Scenario: Mark read
- **WHEN** `rss_mark_read` is called with two ids
- **THEN** both items are marked read without confirmation

### Requirement: Full text by article id
`rss.fetch_full_text(articleId)` SHALL look up the article's link from FreshRSS and extract readable text with SmartReader. It SHALL NOT accept a URL argument.

#### Scenario: URL argument rejected
- **WHEN** the model passes a URL instead of an article id
- **THEN** the tool fails validation and no HTTP request to that URL is made

### Requirement: Digest as skill and template
The RSS digest used by the briefing SHALL be shipped as files only: a skill `rss-digest` (fetch unread since last digest, score, pick top N, fetch full text, summarize and cluster, produce short and long versions, mark processed items read) and a template `rss-scorer` (`fast` tier, no tools) used via `agent.delegate` for relevance scoring against interests from memory. There SHALL be no RSS-digest-specific code.

#### Scenario: Scoring by subagent
- **WHEN** a run follows the `rss-digest` skill
- **THEN** scoring happens through `agent_delegate` with template `rss-scorer`

