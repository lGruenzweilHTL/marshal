# Connect web search (SearXNG)

Let Marshal look things up on the web through your own SearXNG instance, without opening any result page.

## Before you start

- A running [SearXNG](https://docs.searxng.org/) instance reachable from Marshal.
- The instance on your LAN or a VPN only. Exposed to the internet, SearXNG is an open search proxy for anyone.
- Optional: a reverse proxy in front of SearXNG that checks an `Authorization` header. Store the header value as a secret, for example `/run/secrets/searxng_auth`.

## Steps

### 1. Enable JSON output in SearXNG

Marshal uses SearXNG's JSON API. SearXNG serves only HTML by default. In SearXNG's `settings.yml`, allow JSON:

```yaml
search:
  formats:
    - html
    - json
```

Restart SearXNG. A request to `<url>/search?q=test&format=json` now returns JSON instead of `403 Forbidden`.

### 2. Configure Marshal

In `/config/marshal.yaml`:

```yaml
web:
  searxng:
    url: http://searxng.local:8080
    authHeaderSecret: searxng_auth      # optional
    engines: [google, bing, duckduckgo, wikipedia]
    maxResults: 8
```

Only `url` is required. Without the `web` section, the web tools stay hidden. The keys and their ranges are in the [`web` reference](../../configuration/marshal-yaml/integrations.md#web).

### 3. Choose the engines

Marshal sends the `engines` list with every request, so SearXNG asks only those engines. Keep the list to large public engines, such as the default `google`, `bing`, `duckduckgo` and `wikipedia`.

Do not add small or self-hosted third-party engines. The query text reaches every engine on the list (see [Privacy](#privacy)). An engine run by a stranger would see your queries, including queries in a run that read untrusted content.

### 4. Restart Marshal

## Tools

Both web tools are safe, in tainted and untainted runs alike.

| Tool | What it does |
|---|---|
| `web.search(query, limit?)` | One search through SearXNG. Returns title, snippet, URL, domain and a result id per hit. `query` at most 300 characters, `limit` 1 to `maxResults`. |
| `web.answer(question, answerType, values?)` | Searches for the question and gives only the titles and snippets to the [quarantined extractor](../../concepts/untrusted-content.md#extraction-without-reading). Returns the answer (or `unknown`) and the domains of the results. |

### Results are untrusted

Result titles and snippets are written by strangers. `web.search` output is wrapped as untrusted data and taints the run with source `web`.

`web.answer` keeps the run untainted for typed answers (`boolean`, `number`, `integer`, `date`, `datetime`, `enum`). A `text` answer taints the run with source `web`. Ask "When does the Christmas market open?" as a `date`, and a reminder the model then creates needs no confirmation.

`agent.extract` also takes `web:<result id>` references to results of the current conversation. It reads only the title and snippet. Results are kept for one hour, then a reference fails with `extract.not_found`.

### No page is opened

Marshal makes exactly one HTTP request per search, to SearXNG. It never fetches a result URL, so injected text cannot make Marshal visit a page an attacker chose.

## Privacy

The query text is sent to the configured engines through SearXNG, in every run. This includes runs that read mail, RSS or notes. Search queries are not confirm tier: they reach the engines on your list, not a host an attacker picks, and a confirmation prompt would not keep the text private.

If a question must not leave your network, do not ask Marshal to search for it. To turn web search off, remove the `web` section.

## Limits

- Snippets are short. `web.answer` often returns `unknown`; the model can then fall back to `web.search`.
- Upstream engines rate-limit SearXNG. Marshal then reports the service as unavailable (`web.unavailable`) and does not retry beyond its resilience policy.
- Results are not ranked again by Marshal.

## Check that it works

1. Ask "Search the web for the Klipper release notes." The model calls `web_search` and lists results with their domains.
2. Ask "On which date is Easter Sunday next year?" The model calls `web_answer` with `date`. The run detail shows no `web` taint.

## Troubleshooting

- **No `web_` tools.** `web.searxng.url` is missing.
- **Every search fails with `web.unavailable`.** JSON output is not enabled in SearXNG (it answers `403`), the URL is wrong, or the reverse proxy rejects the header. Check that `authHeaderSecret` resolves.
- **Few or no results.** The upstream engines block the SearXNG instance. Check SearXNG's own logs and engine statistics.

## Related

- [Untrusted content](../../concepts/untrusted-content.md)
- [Privacy](../../concepts/privacy.md)
- [Secrets](../secrets.md)
- [Integration sections reference](../../configuration/marshal-yaml/integrations.md#web)
