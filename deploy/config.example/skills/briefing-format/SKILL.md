---
name: briefing-format
description: What goes into the morning briefing and how its spoken and written versions are shaped. Use when preparing the briefing, and when the user asks for the briefing or the day's overview (read the prepared one instead of making a new one).
---

# Briefing format

## On request ("good morning", "what's up today?")

Do not prepare a new briefing in the conversation. Call `tasks_status` with `briefing`. If its result is from today
and less than two hours old, answer with its spoken version as it is. Otherwise call `tasks_run_now` with
`briefing`; the task prepares it in the background and delivers it through its deliveries.

## Gather (while preparing the briefing)

1. Call `calendar_list_events` for today (from now until midnight) and the first event of tomorrow.
2. Call `mail_search` for unread mail from the last 24 hours, and for drafts Marshal created that still wait in the
   drafts folders. Read a message with `mail_read_message` only when its subject is not enough to tell why it matters.
3. Load the `rss-digest` skill and follow it; keep its short and long versions.
4. Call `ha_homeassistant__GetLiveContext` for the weather forecast and anything in the house that needs attention
   (open windows, low batteries, a finished washing machine).
5. Call `memory_recall` with "plans and deadlines for today" for commitments that are not in the calendar.

Leave out any part that has nothing to report; never say "no news from X".

## Answer

Answer in the user's language, in two parts separated by one blank line.

**First paragraph: the spoken version.** It is read aloud as it is, so it is exactly one paragraph of plain
sentences: no lists, no markdown, no links, no ids. It takes 60 to 90 seconds to say (about 150 to 220 words).
Put the most important thing first: the first appointment and anything with a deadline today, then mail that needs an
answer, then the weather, then the two or three most interesting feed topics. End with one short sentence offering
more detail ("Ask me for more about any of it.").

**Everything after it: the written version** for the inbox and the web UI, in markdown:

- **Today**: the day's appointments with times and places.
- **Mail**: one line per message worth reading (sender, subject, why it matters), then the waiting drafts with the
  recipient, subject and the first sentence as a preview.
- **Feeds**: the long version of the RSS digest, each article with its feed name and a link to the article.
- **House and weather**: what needs attention and the forecast.

Keep the written version under 600 words.
