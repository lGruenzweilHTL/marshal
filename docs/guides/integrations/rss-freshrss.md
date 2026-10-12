# Connect FreshRSS

Let Marshal read your news from FreshRSS, mark articles read, and fetch full article text for the daily digest.

## Before you start

- A running FreshRSS reachable from Marshal. The API lives at `<url>/api/greader.php` (Google Reader-compatible API).
- In FreshRSS, enable the API (Settings, Authentication, "Allow API access") and set an **API password** under Profile, API management. This is not your login password.
- The API password stored as a secret, for example `/run/secrets/freshrss_api`.

## Steps

### 1. Configure FreshRSS

In `/config/marshal.yaml`:

```yaml
freshrss:
  url: http://freshrss.local
  userName: lukas
  apiPasswordSecret: freshrss_api
  searchWindow: 500
  fullText: { maxBytes: 2097152, timeout: "00:00:15", maxCharacters: 20000 }
```

`url`, `userName` and the resolvable secret are required together. The RSS tools stay hidden until FreshRSS is configured. The remaining keys and their ranges are in the [`freshrss` reference](../../configuration/marshal-yaml/integrations.md#freshrss).

### 2. Restart Marshal

## Tools

All RSS tools are safe. Article content is untrusted, so reading it taints the run with source `rss`.

| Tool | What it does |
|---|---|
| `rss.list_feeds` | Subscribed feeds with their categories. |
| `rss.list_categories` | Feed categories. |
| `rss.get_unread(category?, since?, limit)` | Unread articles, newest first, with a short summary. `limit` 1 to 50. |
| `rss.get_article(id)` | One article's feed content. |
| `rss.search(query, limit)` | Words in title or summary across the most recent `searchWindow` articles, read and unread. FreshRSS's API has no search, so Marshal filters locally. |
| `rss.mark_read(ids)` | Marks articles as read, without confirmation. |
| `rss.fetch_full_text(articleId)` | Fetches the article's page and extracts readable text. |

Subscribing, unsubscribing and deleting do not exist.

### Full text by article id only

`rss.fetch_full_text` takes an article id, never a URL. Marshal looks up the article's link in FreshRSS and extracts the text with SmartReader. A URL argument fails validation and no request is made. This keeps the model from fetching a page that a prompt chose. The page read is limited by `fullText.maxBytes`, `fullText.timeout` and the returned text by `fullText.maxCharacters`.

## The RSS digest

The digest is shipped as files only, with no digest-specific code: the skill `rss-digest` fetches unread articles since the last digest, scores them, picks the top items, fetches full text, summarizes and clusters, and marks processed items read. Scoring runs through `agent.delegate` with the template `rss-scorer` (`fast` tier, no tools) against the interests in your memory. Use the digest in a [daily briefing](../tasks/daily-briefing.md). See [skills](../skills.md) and [agent templates](../agent-templates.md).

## Limits

- Pagination follows FreshRSS's `continuation` until the requested limit.
- `rss.search` sees only the newest `searchWindow` articles (default 500, at most 5000).
- Full text depends on the site. Paywalled or script-rendered pages yield little text.

## Check that it works

1. Ask "what feeds do I follow?" The model calls `rss_list_feeds`.
2. Ask "what's new in the tech category?" You get unread titles with summaries.
3. Ask "mark those as read." FreshRSS shows them read.

## Troubleshooting

- **No `rss_` tools.** `freshrss.url` or the secret is missing, or the secret does not resolve.
- **Authentication fails.** You used the login password. Use the API password.
- **Empty list.** FreshRSS API access is off for the user.

## Related

- [Untrusted content](../../concepts/untrusted-content.md)
- [Daily briefing](../tasks/daily-briefing.md)
- [Secrets](../secrets.md)
- [Integration sections reference](../../configuration/marshal-yaml/integrations.md#freshrss)
