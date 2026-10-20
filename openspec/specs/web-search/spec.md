# web-search Specification

## Purpose
Lets Marshal look things up on the web through a self-hosted SearXNG instance, with results handled as untrusted data and typed facts available without tainting the run.

## Requirements

### Requirement: Web search configuration
Web search SHALL be configured as `web: { searxng: { url, authHeaderSecret?, engines?, maxResults? } }`. `url` SHALL be an absolute `http` or `https` URL; `authHeaderSecret` SHALL name a secret holding an `Authorization` header value; `engines` SHALL be a list of SearXNG engine names sent with every request (default `google`, `bing`, `duckduckgo`, `wikipedia`); `maxResults` SHALL be 1–20 (default 8). Invalid values SHALL fail startup naming the key. Without the section, no `web` tool SHALL exist.

#### Scenario: Not configured
- **WHEN** `marshal.yaml` has no `web` section
- **THEN** neither `web_search` nor `web_answer` is in any tool list

### Requirement: Search tool
The system SHALL provide `web.search(query, limit?)` (safe, `untrustedOutput`, taint source `web`). It SHALL send one request to SearXNG's JSON API with the query (at most 300 characters), the configured engines and the turn's language, and SHALL return up to `limit` (default `maxResults`) results with title, snippet, URL, domain and a result id. It SHALL NOT fetch any result URL. Results SHALL be kept for the session, addressable by result id, for at most one hour. A failure of SearXNG SHALL return an error result naming the service as unavailable.

#### Scenario: Search taints
- **WHEN** an untainted run calls `web_search`
- **THEN** the results are wrapped as untrusted data and the run is tainted with source `web`

#### Scenario: No page fetch
- **WHEN** a search returns ten results
- **THEN** Marshal made exactly one HTTP request, to SearXNG

### Requirement: Answer tool
The system SHALL provide `web.answer(question, answerType, values?)` (safe). It SHALL search for the question like `web.search`, give only the results' titles and snippets to the quarantined extractor with the question and answer type, and SHALL return the answer and the domains of the results. The run's taint SHALL follow the extractor's rule: typed answers SHALL NOT taint; `text` answers SHALL taint with source `web` and be wrapped as untrusted data.

#### Scenario: Date answer
- **WHEN** an untainted run calls `web_answer` with "When does the city's Christmas market open this year?" and `date`
- **THEN** the result is a date or `unknown` with the domains used, and the run stays untainted

#### Scenario: Text answer
- **WHEN** a run calls `web_answer` with answer type `text`
- **THEN** the run is tainted with source `web`

### Requirement: Queries are not confirm tier
Search queries SHALL be safe in tainted and untainted runs. The web search guide SHALL state that query text is sent to the configured search engines through SearXNG.

#### Scenario: Search in a tainted run
- **WHEN** a run that read mail calls `web_search`
- **THEN** the call executes without confirmation
