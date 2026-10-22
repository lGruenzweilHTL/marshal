---
icon: fa-solid fa-toolbox
---

# Tools

Every tool the model can get, grouped by integration, with its risk tier, arguments, result and the conditions that hide it.

## Conventions

- **Tool id.** Every tool has an internal id `<namespace>.<name>`. Risk rules, satellite policies, template allowlists, run records and the audit log use it. The model sees the name `<namespace>_<name>` (for example `mail.search` is `mail_search`). This page lists the id.
- **Tier.** The default [risk tier](risk-tiers.md) of the tool. A rule in `risk.yaml` can raise or lower it, and a tainted run raises `persists` tools from `safe` to `confirm`.
- **Untrusted.** The result is third-party content. Marshal wraps it as untrusted data and the run becomes tainted with the source shown. See [untrusted content](../concepts/untrusted-content.md).
- **Persists.** The tool stores state that the agent acts on later. In a tainted run its calls need confirmation.
- **Hidden when.** Marshal removes the tool from the tool list before each model call. The guard enforces the same rules on every call. Besides the conditions below, a tool is also hidden when a risk rule marks it `forbidden`, when the active [agent template](../guides/agent-templates.md) does not allow it, when a satellite's `denyTools` matches it, when its tier resolves to `confirm` on a satellite whose `denyConfirm` matches it, and, for confirm-tier tools, inside subagents. See [tool visibility](../concepts/tool-visibility.md).
- Arguments marked with `?` are optional. Unknown arguments are rejected by the `tasks.*` tools, the `printer.*` tools and `rss.fetch_full_text`.

## Summary

| Namespace | Tools | Source |
|---|---|---|
| `agent` | `ask_advisor`, `move_to_background`, `load_skill`, `delegate` | Built in |
| `memory` | `remember`, `recall`, `correct_project` | Built in |
| `tasks` | `create`, `schedule_wakeup`, `watch`, `update`, `run_now`, `list`, `status`, `cancel`, `stop_all`, `run_executor` | Built in |
| `notifications` | `notify`, `inbox_list`, `inbox_consume` | Built in |
| `timers` | `start`, `cancel`, `status` | Built in (Home Assistant) |
| `places` | `find`, `create_here` | Built in (Home Assistant) |
| `energy` | `usage` | Built in (Home Assistant) |
| `calendar` | `list_calendars`, `list_events`, `find_free_time`, `create_event`, `update_event` | Built in (Home Assistant, CalDAV) |
| `music` | `search`, `play`, `queue`, `control`, `transfer`, `group`, `ungroup` | Built in (Music Assistant through Home Assistant) |
| `printer` | `status`, `list_files`, `job_queue`, `history`, `snapshot`, `inspect`, `pause`, `emergency_stop`, `start_print`, `resume`, `cancel`, `set_temperature`, `home`, `move`, `edit_queue`, `run_macro` | Built in (Moonraker) |
| `mail` | `search`, `read_message`, `read_thread`, `list_folders`, `list_labels`, `create_draft`, `update_draft`, `save_attachment` | Built in |
| `rss` | `list_feeds`, `list_categories`, `get_unread`, `get_article`, `search`, `mark_read`, `fetch_full_text` | Built in (FreshRSS) |
| `notes` | `list`, `read`, `search` | Built in |
| `web` | `search`, `answer` | Built in (SearXNG) |
| `media` | `search`, `request`, `requests`, `upcoming`, `downloads` | Built in (Seerr, Sonarr, Radarr) |
| `infra` | `overview`, `guests`, `storage`, `tasks` | Built in (Proxmox VE) |
| `repos` | `prs`, `pr`, `issues`, `issue`, `runs`, `run_log` | Built in (GitHub) |
| `artifacts` | `list`, `show`, `inspect` | Built in |
| `system` | `health` | Built in |
| `maintenance` | `status`, `done` | Built in |
| `ha` and others | Whatever the server reports | [MCP servers](#mcp-server-tools) |

The namespaces `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`, `system`, `maintenance` and `web` are reserved for built-in tools. An MCP server with one of these ids is rejected.

Marshal has no tool that sends mail, no tool that sends raw G-code and no tool that deletes mail, files or calendar events. These capabilities do not exist as code paths.

## Agent (`agent`)

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `agent.ask_advisor` | safe | `question`, `context` | The text answer of one tool-less call to the `advisor` model tier. The advisor sees only the question and your context summary. | The turn is a voice turn; no `advisor` model is configured; the per-run call limit is reached (`agent.advisor.maxCallsPerRun`, or the template's `maxAdvisorCalls`); the owner's daily advisor cost cap is reached. |
| `agent.move_to_background` | safe | `statusMessage` | Ends the turn with the status message and continues the run as a background task. The user receives the final answer as a notification. | The turn is not interactive or is a subagent turn, or background work is not available. |
| `agent.load_skill` | safe | `name`, `file?` | The skill body with the list of its files, or one file of the skill. Output is trusted. | The active template allows no skill, or no skill exists. |
| `agent.find_tools` | safe | `query` | Loads the tool namespaces with visible tools that best match the query (a namespace named in the query first) and returns their names and descriptions. Their tools are offered from the next model call. | Tool retrieval is disabled or skipped for the run, or every namespace with visible tools is already loaded. |
| `agent.delegate` | safe | `template`, `task` | The final text of a subagent that runs the named [agent template](../guides/agent-templates.md). A tainted caller gets the text wrapped as untrusted data. The subagent never runs confirm-tier actions. | The run is at the delegation depth limit (`agent.maxDelegationDepth`), or no template other than the default exists. |

## Memory (`memory`)

Memory output is trusted: only you or a confirmed write puts content into memory.

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `memory.remember` | safe, persists | `type`, `title`, `content`, `importance?`, `scope?`, `project?`, `sourceRef?`, `validFrom?`, `validTo?` | Stores a chunk. The same type and title update the existing chunk. `type` is `fact`, `preference`, `event`, `procedure`, `person` or `device`; `importance` is 1 to 5 (default 3); `scope` is `general` or `project`; `validFrom` and `validTo` are dates or ISO 8601 date-times in the owner's time zone (default: valid from now, open). The tool cannot pin a chunk. | Never. |
| `memory.recall` | safe | `query`, `type?`, `project?`, `limit?`, `asOf?` | Chunks matching the query, including pinned chunks. `limit` is 1 to 30 (default 10). Without `asOf` only chunks valid now; with `asOf` (date or date-time) what was valid then, including closed chunks and earlier versions, each with its valid time. | Never. |
| `memory.correct_project` | safe | `kind`, `project?` | Records your correction of the inferred project. `kind` is `rejected`, `switched` (needs `project`) or `confirmed`. | No project exists. |

## Tasks (`tasks`)

All task tools are safe. Tools that store work for later are `persists`, and an `overrideQuietHoursUntil` argument always raises the call to `confirm` with reason `quiet_hours_override`. Tools that start work are hidden while the kill switch is active. Inside a task run, a new task is a child task.

`trigger` is an object `{ kind, time?, expression?, task? }`. `kind` is `now`, `at` (with local `time`), `cron` (with a five-field `expression` in local time) or `after` (with the id or name of another `task`). `deliver` is a list of deliveries: `channel` (`voice`, `push`, `inbox`, `silent`), `target?` (voice only: satellite entity id), `priority?` (`normal` or `low`), `followUp?`, `when?` (conditions), `expires?` (local deadline) and `fallback?`.

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `tasks.create` | safe, persists | `goal`, `trigger?`, `name?`, `template?`, `notes?`, `tools?`, `prepare?`, `deliver?`, `overrideQuietHoursUntil?` | The created task as a summary. | Kill switch active; inside a task run, the task depth limit (`tasks.maxDepth`) or the child limit (`tasks.maxActiveChildren`) is reached. |
| `tasks.schedule_wakeup` | safe, persists | `at?` or `inMinutes?`, `notes?` | The wake time. Ends the current run and runs the task again later with its goal and the new notes. | The call is not inside a background task run, or the kill switch is active. |
| `tasks.watch` | safe, persists | `filter`, `goal`, `name?`, `once?`, `deliver?`, `overrideQuietHoursUntil?` | The created event-triggered task. `filter` needs `source` and can carry `type`, an `entity` glob and payload conditions such as `to` and `from`. | Same as `tasks.create`. |
| `tasks.update` | safe, persists | `task`, `goal?`, `trigger?`, `deliver?`, `template?`, `name?`, `prepare?`, `overrideQuietHoursUntil?` | The updated task. | No task exists. |
| `tasks.run_now` | safe | `task` | `{ started, task }`. Fails with `tasks.running` while the task runs. | Kill switch active. |
| `tasks.list` | safe | `status?` | Up to 50 tasks. `status` is `active`, `pending`, `running`, `waiting`, `done`, `failed` or `cancelled`. | Never. |
| `tasks.status` | safe | `task` | The task's status and its latest result with its age. | Never. |
| `tasks.cancel` | safe | `task` | The number of cancelled tasks. Cancels unfinished child tasks too. | Never. |
| `tasks.stop_all` | safe | none | Kill switch: cancels all background tasks and pauses new ones until you resume them in the web UI. | Never. |
| `tasks.run_executor` | confirm, persists | `executor`, `spec`, `onDoneGoal`, `deliver?` | Starts long external work in an [executor](../installation/executor-host.md) and runs `onDoneGoal` when it finishes. `executor` is one of the configured executor names (currently `claude_code`). | No executor is configured; kill switch active; inside a task run, the depth or child limit is reached. |

## Notifications (`notifications`)

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `notifications.notify` | safe | `message`, `priority?`, `deliver?` | `{ sent: true }`. `priority` is `normal` (default) or `low`; `critical` fails validation. `deliver` is `{ channel, target? }` with `channel` `voice`, `push` or `inbox`; only voice has a target. `low` never uses voice. | Never. |
| `notifications.inbox_list` | safe, untrusted (`notifications`) | none | Up to 50 unread inbox items, newest first, with id, source, priority, text and time. | Never. |
| `notifications.inbox_consume` | safe | `ids` | `{ consumed }`. Marks the items as handled. | Never. |

See [notification priorities](notifications-priorities.md).

## Home Assistant tools

Home Assistant entity control comes from HA's MCP server, see [MCP server tools](#mcp-server-tools). The built-in tools below use HA's REST and websocket APIs.

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `timers.start` | safe | `duration`, `name?` | HA's spoken answer. `duration` accepts `10m`, `1h30m`, `90s` or ISO 8601 (`PT1H30M`). Runs on the device the user is talking to. | The turn has no device, or HA's MCP server already offers `HassStartTimer`. |
| `timers.cancel` | safe | `name?` | HA's spoken answer. Cancels by name, or the only timer. | Same as `timers.start`. |
| `timers.status` | safe | none | The timers running on the device. | Same as `timers.start`. |
| `places.find` | safe | `query?` | HA zones with radius, whether you are inside, and `personState`, the value your person entity takes there. | No `ha.url` configured. Fails with `ha.unavailable` while HA is not connected. |
| `places.create_here` | confirm | `name`, `radius?` | Creates a zone at your person entity's current position. `radius` is 50 to 1000 m (default 150). | No `ha.url`, or no person entity is set for the owner. |
| `energy.usage` | safe | `period`, `from?`, `to?`, `compareWith?` | Grid import and export, solar, battery, household consumption, cost and top devices. `period` is `today`, `yesterday`, `week`, `month`, `last_week`, `last_month` or `range` (with `from` and `to`). `compareWith: previous` adds the previous period. | No `ha.url`, or the HA energy dashboard has no sources. |

## Calendar (`calendar`)

Calendars come from Home Assistant calendar entities (`calendar.*`) and CalDAV accounts (`caldav:<account>/<calendar>`). Times are local (`2026-09-30T09:00`) or a date (`2026-09-30`) for whole days. Setup: [use your calendars](../guides/integrations/calendar.md).

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `calendar.list_calendars` | safe | none | Calendars with `id`, `name`, `canCreate`, `canUpdate`. | No calendar is configured. |
| `calendar.list_events` | safe, untrusted (`calendar`) | `calendar?`, `start`, `end` | Events in the range, oldest first, from every calendar or one. Recurring events come as occurrences with `recurrenceId`, `recurring`, `seriesId` and `originalStart`. CalDAV events add `organizer` and `attendees` (`name`, `address`, `role`, `status`: `needs-action`, `accepted`, `declined`, `tentative`, `delegated`). | No calendar is configured. |
| `calendar.find_free_time` | safe | `start`, `end`, `duration` (minutes), `calendars?`, `within?` (`08:00-18:00`) | Up to 20 free slots of at least `duration`, earliest first, and the calendars read. Events marked free and meetings the account declined are not busy. Only times and calendar ids, so not untrusted. | No calendar is configured. |
| `calendar.create_event` | confirm | `calendar`, `summary`, `start`, `end`, `description?`, `location?` | Creates the event. | No calendar can create events. |
| `calendar.update_event` | confirm | `calendar`, `eventId`, `summary`, `start`, `end`, `recurrenceId?`, `scope?`, `description?`, `location?` | Replaces the event. Pass every field. `scope: occurrence` (default with `recurrenceId`) changes one instance, `scope: series` every instance. `scope` fails with `calendar.invalid_arguments` on calendars that cannot change single instances (Home Assistant). | No calendar can update events. |

## Music (`music`)

All music tools are safe and fail with `ha.unreachable` when HA cannot be reached, and with `music.failed` when HA or Music Assistant rejects the call. Tools that take `speaker` use the speaker of the asking satellite when you omit it. Only `music.search` and `music.queue` return third-party text, so only they are untrusted (taint source `music`).

| Tool id | Arguments | Returns |
|---|---|---|
| `music.search` | `query`, `type?` | Up to 10 tracks, albums, artists, playlists and radio stations each, with name, artists, album and `uri`. `type` is `track`, `album`, `artist`, `playlist` or `radio`. Untrusted. |
| `music.play` | `item`, `type?`, `speaker?`, `enqueue?`, `radio?` | Plays a search phrase or a Music Assistant `uri`, never a web URL. `enqueue` is `play` (default), `replace`, `next`, `replace_next` or `add`. `radio: true` continues with similar music. |
| `music.queue` | `speaker?` | Playback state, current and next item, queue length, shuffle and repeat. Untrusted. |
| `music.control` | `action`, `speaker?`, `volume?` | `action` is `pause`, `resume`, `next`, `previous`, `stop` or `volume`. `volume` is 0 to 100. |
| `music.transfer` | `to`, `from?` | Moves queue and playback to another speaker. |
| `music.group` | `speakers` | Joins at least two speakers into a synced group. The first speaker leads. |
| `music.ungroup` | `speaker` | Removes a speaker from its group. |

Hidden when `ha.url` is not set or `music.speakers` is empty. `music.play`, `music.queue`, `music.control` and `music.transfer` default to the asking room's speaker; a turn without a satellite speaker must name one (`music.no_speaker`).

## Printers (`printer`)

Every printer tool takes an optional `printer` argument. It defaults to the only configured printer; with several printers and no value, the call fails and lists the names. All printer tools are hidden when no printer is configured.

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `printer.status` | safe | `printer?` | State, job, progress, layer, time left and temperatures, plus the print babysitter state. | No printer. |
| `printer.list_files` | safe | `printer?` | Up to 50 printable files, newest first. | No printer. |
| `printer.job_queue` | safe | `printer?` | The job queue. | No printer. |
| `printer.history` | safe | `printer?`, `limit?` | Recent print jobs, newest first. `limit` is 1 to 50 (default 10). | No printer. |
| `printer.snapshot` | safe | `printer?` | A webcam snapshot as a Markdown image with a same-origin URL. | No printer has a webcam. |
| `printer.inspect` | safe | `printer?`, `question` | The answer of a vision model about the webcam picture. | No vision model in the run's tier, or no webcam. |
| `printer.pause` | safe | `printer?` | Pauses the running print. | No printer. |
| `printer.emergency_stop` | safe | `printer?` | Emergency stop. Klipper needs a restart afterwards. | No printer. |
| `printer.start_print` | confirm | `printer?`, `file` | Starts printing a file from `printer.list_files`. | No printer. |
| `printer.resume` | confirm | `printer?` | Resumes a paused print. | No printer. |
| `printer.cancel` | confirm | `printer?` | Cancels the running print. | No printer. |
| `printer.set_temperature` | confirm | `printer?`, `heater`, `target` | Sets a heater target in degrees C (0 turns it off). `heater` is `extruder` or `bed`. Validated against the printer's limits before any confirmation. | No printer. |
| `printer.home` | confirm | `printer?`, `axes?` | Homes the given axes (`x`, `y`, `z`), or all. | No printer. |
| `printer.move` | confirm | `printer?`, `axis`, `distance` | Moves one axis relative to its position, in mm. | No printer. |
| `printer.edit_queue` | confirm | `printer?`, `action`, `files?`, `job_ids?`, `job_id?` | `action` is `add`, `remove`, `clear`, `pause`, `start` or `jump`. | No printer. |
| `printer.run_macro` | confirm | `printer?`, `name` | Runs an allowlisted Klipper macro without arguments. `name` is limited to the configured macros. | No printer has an allowlisted macro. |

`printer.pause` and `printer.emergency_stop` are safe on purpose: stopping a failing print must not wait for a confirmation. Raw G-code, firmware or host restart and file deletion do not exist as tools.

## Mail (`mail`)

Mail tools never send, delete or move mail. All mail tools are hidden when no mail account exists. `account` is the account alias; without it a search covers all accounts.

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `mail.search` | safe, untrusted (`mail`), sensitive | `account?`, `from?`, `to?`, `subject?`, `text?`, `since?`, `before?`, `unread?`, `folder?`, `limit?` | Message summaries with account alias and message id. `since` and `before` are `YYYY-MM-DD`. `folder` defaults to the inbox. `limit` is 1 to 50 (default 20). | No mail account. |
| `mail.read_message` | safe, untrusted (`mail`), sensitive | `id` | One message as text, with attachments listed by name. | No mail account. |
| `mail.read_thread` | safe, untrusted (`mail`), sensitive | `id` | The whole conversation, oldest first. | No mail account. |
| `mail.list_folders` | safe | `account?` | The account's folders. | No mail account. |
| `mail.list_labels` | safe | `account?` | The account's labels. | No account supports labels. |
| `mail.create_draft` | safe | `account?`, `to`, `subject`, `body`, `replyTo?` | Saves a draft in the account's drafts folder. Nothing is sent. | No mail account. |
| `mail.update_draft` | safe | `id`, `to`, `subject`, `body`, `replyTo?` | Replaces a draft Marshal created. Nothing is sent. | No mail account. |
| `mail.save_attachment` | confirm, persists, sensitive | `message`, `attachment` | Stores one attachment as a tainted artifact (source `mail`) and returns its id. `attachment` is its name or position (1 is the first). | No mail account. |

## RSS (`rss`)

RSS tools read and mark articles. There is no tool to subscribe or delete. All are hidden when FreshRSS is not configured. `rss.fetch_full_text` takes an article id, never a URL.

| Tool id | Tier | Arguments | Returns |
|---|---|---|---|
| `rss.list_feeds` | safe | none | Feeds with title, categories and site URL. |
| `rss.list_categories` | safe | none | Category names. |
| `rss.get_unread` | safe, untrusted (`rss`) | `category?`, `since?`, `limit?` | Unread articles, newest first, with a summary cut at 300 characters. `since` is `YYYY-MM-DD` or ISO 8601. `limit` is 1 to 50 (default 20). |
| `rss.get_article` | safe, untrusted (`rss`) | `id` | One article's feed content. |
| `rss.search` | safe, untrusted (`rss`) | `query`, `limit?` | Recent articles (read and unread) matching words in title or summary. |
| `rss.mark_read` | safe | `ids` | Marks articles as read, without confirmation. |
| `rss.fetch_full_text` | safe, untrusted (`rss`) | `articleId` | Readable full text extracted from the article's web page, cut at a length limit. |

## Notes (`notes`)

All notes tools are safe, untrusted (taint source `notes`) and sensitive. They are hidden when no notes source is configured. Marshal never writes notes.

| Tool id | Arguments | Returns |
|---|---|---|
| `notes.list` | `folder?` | Up to 500 notes with path, size and modified time. |
| `notes.read` | `path` | One note. |
| `notes.search` | `query`, `limit?` | Matching sections found by meaning and keywords. `limit` is 1 to 20. |

## Web search (`web`)

Web tools are safe in every run, also in a tainted one. They are hidden when `web.searxng` is not configured. Marshal never opens a result URL. See [web search](../guides/integrations/web-search.md).

| Tool id | Tier | Arguments | Returns |
|---|---|---|---|
| `web.search` | safe, untrusted (`web`) | `query`, `limit?` | Up to `limit` results (default and maximum `web.searxng.maxResults`) with result id, title, snippet, URL and domain. `query` is at most 300 characters. Results stay addressable as `web:<result id>` for one hour. |
| `web.answer` | safe | `question`, `answerType`, `values?` | `{ answer, domains }`. Only titles and snippets reach the quarantined extractor. Typed answers do not taint; a `text` answer taints with `web` and is wrapped as untrusted data. `values` is required for `enum`. |

## Media (`media`)

All media tools are untrusted (taint source `media`) and hidden when `media.seerr.url` is not set. Marshal only reads Sonarr and Radarr and creates Seerr requests; it cannot delete or change them.

| Tool id | Tier | Arguments | Returns |
|---|---|---|---|
| `media.search` | safe | `query`, `type?` | Up to 10 movies and shows with `tmdbId`, type, title, year, a short overview, season count, availability and a Jellyfin link. `type` is `movie` or `show`. |
| `media.request` | confirm | `type`, `tmdbId`, `title`, `year`, `seasons?` | Creates a Seerr request. After confirmation, Marshal checks that the id still belongs to that title and year and fails with `media.title_mismatch` otherwise. Fails with `media.already_available` or `media.already_requested`. A show without `seasons` requests all seasons. |
| `media.requests` | safe | `status?` | Up to 20 newest requests. `status` is `pending`, `approved`, `declined`, `failed` or `completed`. |
| `media.upcoming` | safe | `days?` | Upcoming episodes (Sonarr) and movie releases (Radarr), at most 50. `days` is 1 to 30 (default 7). |
| `media.downloads` | safe | none | The Sonarr and Radarr download queues. |

## Infrastructure (`infra`)

All infra tools are safe and trusted. They only send `GET` requests to Proxmox VE and fail with `infra.unavailable` when the API is unreachable or rejects the token. They are hidden when `proxmox.url` is not set.

| Tool id | Arguments | Returns |
|---|---|---|
| `infra.overview` | none | Nodes with CPU, memory and uptime, guest counts by status, and storages more than 80 % full. |
| `infra.guests` | `node?`, `status?` | VMs and containers with id, name, node, status, CPU, memory and uptime. `status` is `running`, `stopped` or `paused`. |
| `infra.storage` | none | Storages with type, used and total size in GB and percent used. Shared storages appear once. |
| `infra.tasks` | `failedOnly?`, `since?` | The newest 50 Proxmox tasks (backups, migrations, starts) with type, guest id, node, user, times and status. `since` is an ISO 8601 time. |

## GitHub repositories (`repos`)

All repos tools are safe and untrusted (taint source `github`). Every tool takes a required `repo` argument, an enum of the configured repo ids. They are hidden when no repository is configured under `github.repos`. A value outside the allowlist fails with `repos.invalid_arguments` and no request reaches GitHub.

| Tool id | Arguments | Returns |
|---|---|---|
| `repos.prs` | `repo`, `state?` | Up to 30 newest pull requests. `state` is `open` (default), `closed` or `all`. |
| `repos.pr` | `repo`, `number` | Title, author, state, branches, body and change counts. The diff is kept as a `diff` artifact (`diffArtifactId`). |
| `repos.issues` | `repo`, `state?`, `query?` | Up to 30 newest issues without pull requests. `query` filters titles among the 100 newest. |
| `repos.issue` | `repo`, `number` | Title, author, state, labels, body and the 20 newest comments. |
| `repos.runs` | `repo`, `branch?` | Up to 20 newest workflow runs. |
| `repos.run_log` | `repo`, `runId` | For each failed job its failed steps and the last 50 log lines. The full log is kept as a `code` artifact (`logArtifactId`). |

## Artifacts (`artifacts`)

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `artifacts.list` | safe, untrusted (`artifact`) | `kind?`, `source?`, `query?` | Up to 50 newest artifacts with id, name, kind, source, size and creation time. `kind` is `image`, `svg`, `video`, `model3d`, `code`, `diff`, `pdf`, `markdown`, `html` or `other`; `source` is `upload`, `executor`, `printer`, `mail` or `github`. | Never. |
| `artifacts.show` | safe | `id`, `page?` | In a web turn, shows the artifact in the chat. In a satellite turn, plays its rendition on the display in the room (`page` selects the page, default 1). The model cannot choose another display. | The turn is not a web turn and the satellite has no display. |
| `artifacts.inspect` | safe, untrusted (`artifact`) | `id`, `question`, `page?` | The answer of a vision model about one rendition page, and the page count. Fails with `artifacts.no_rendition` when the rendition is not an image. | Neither the run's tier nor `standard` has a vision model. |

## System (`system`)

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `system.health` | safe | none | The current [health warnings](health-endpoints.md#health-warnings): component, code, severity, the message in the run's language and since when. Trusted output: the messages are Marshal's own. | There are no warnings. |

## Maintenance (`maintenance`)

| Tool id | Tier | Arguments | Returns | Hidden when |
|---|---|---|---|---|
| `maintenance.status` | safe | `item?` (item id; omit for all) | Per item: name, usage, interval, unit, state (`ok`, `soon`, `due`) and when it was last done. | No `maintenance.items` are configured. |
| `maintenance.done` | safe, persists | `item` | The item after marking it done: usage `0`, state `ok`. | No `maintenance.items` are configured. |

See [track maintenance by usage](../guides/maintenance.md).

## MCP server tools

Tools from [MCP servers](../configuration/mcp-servers.md) use the server id (or `toolPrefix`) as namespace, without an `mcp.` prefix. Names are kept as the server reports them, with characters outside `[a-zA-Z0-9_-]` replaced by `_`. A model-facing name longer than 64 characters is shortened and ends in a hash. For example, the Home Assistant MCP server (id `ha`) reports `intent__HassTurnOn`, which becomes the id `ha.intent__HassTurnOn` and the model name `ha_intent__HassTurnOn`.

- **Tier.** External tools default to `confirm`. Add rules to `risk.yaml` to change that, for example `{ tool: "ha.*" } -> safe`. The example `risk.yaml` keeps locks, alarm panels and garage doors, doors and gates on `confirm`.
- **Untrusted.** Tools of a server with `untrusted: true`, and tools matching `untrustedTools`, taint the run with source `mcp:<server id>`. Other tools do not.
- **Hidden when.** The server is down, failed discovery or was rejected. The tools reappear after Marshal reconnects. `/health` shows a warning while a server is down. A tool that is new or changed after the server's first discovery is hidden until an admin approves it ([tool pinning](../configuration/mcp-servers.md#tool-pinning)).
- **Input and server tasks.** A server's input request never reaches the model; the tool result says the user was asked in the web UI. A call the server continues as a server task returns a result saying the work continues in the background; the outcome arrives later through a waiting task. See [input requests](../configuration/mcp-servers.md#input-requests) and [server tasks](../configuration/mcp-servers.md#server-tasks).
- **Home Assistant.** `ha.*` tools resolve their targets (name, area, floor, domain, device class) to concrete entities, so rules can match on `domain` and `deviceClass`. A call whose targets cannot be resolved is `confirm` with reason `unresolved_target`. The context builder gives the model the states of nearby entities; the model calls `ha.homeassistant__GetLiveContext` for more.
- **Demo mode.** When Home Assistant is simulated and no MCP server `ha` is configured, [demo mode](../guides/demo-mode.md) offers `ha.intent__HassTurnOn`, `ha.intent__HassTurnOff`, `ha.intent__HassLightSet`, `ha.intent__HassClimateSetTemperature` and `ha.homeassistant__GetLiveContext` itself, backed by the simulated states. They default to `confirm` like the server's tools, so the same `risk.yaml` rules apply.

## Related

- [Risk tiers](risk-tiers.md)
- [Tool visibility](../concepts/tool-visibility.md)
- [Adding a tool](../development/adding-a-tool.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
