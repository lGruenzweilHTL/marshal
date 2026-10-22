# Daily briefing

Set up the morning briefing from the example task, template and skills, and tune what it says and where you hear it.

The briefing has no special code. It is a task named `briefing` that uses the `briefing` template and the `briefing-format` and `rss-digest` skills.

## Before you start

- Copy `deploy/config.example/templates/` and `deploy/config.example/skills/` into `/config` (at least `briefing.md`, `rss-scorer.md`, `briefing-format/` and `rss-digest/`). Both hot-reload.
- The integrations the briefing reads are connected: calendar, [mail](../integrations/mail.md), [FreshRSS](../integrations/rss-freshrss.md) and [Home Assistant](../integrations/home-assistant.md). A part with no data source is left out.
- Your language is set (`owner.language`); the briefing answers in it.

## How the pieces fit

| Piece | File | Job |
|---|---|---|
| Task | `tasks/briefing.yaml` (arguments of `tasks.create`) | When to run and where to deliver. |
| Template | `templates/briefing.md` | Tool allowlist, model tier `standard`, `maxSteps: 16`, no advisor. Tells the agent nobody is listening, so it asks no questions. |
| Skill `briefing-format` | `skills/briefing-format/SKILL.md` | What to gather and the shape of the answer. |
| Skill `rss-digest` | `skills/rss-digest/SKILL.md` | Scores unread articles with the `rss-scorer` subagent, summarizes the top ones, marks them read. |

The template allows only reading tools plus `rss.mark_read`: `calendar.list_events`, `mail.search`, `mail.read_message`, `rss.get_unread`, `rss.fetch_full_text`, `rss.mark_read`, `memory.recall`, the Home Assistant live-context tool, `system.health`, `agent.load_skill` and `agent.delegate`. Mail, feeds and calendar entries are material to summarize, never instructions.

## Steps

::: steps
1. **Create the task**

   Ask: "Create the morning briefing task with these settings", then paste the example.

   ```yaml
   name: briefing
   template: briefing
   goal: >-
     Prepare today's morning briefing: appointments, mail that needs attention and waiting drafts, the RSS digest,
     the weather and the house. Follow the briefing-format skill.
   trigger:
     kind: cron
     expression: "0 6 * * *"
   deliver:
     - channel: voice
       followUp: true
       when:
         - state($person = home)
         - state(binary_sensor.kitchen_occupancy = on)
       expires: "11:00"
       fallback: push
     - channel: inbox
   ```

   Change `binary_sensor.kitchen_occupancy` to an occupancy sensor of your room. `$person` resolves to your person entity (notification settings).

2. **Understand the deliveries**

   At 03:00 a preparation run collects the appointments and the RSS digest (`prepare.before: 3h`). The briefing is prepared at 06:00 with these notes and checks mail, the weather and the house fresh. The spoken version plays when both conditions hold, for example when you walk into the kitchen at 07:30, and the satellite then listens for "tell me more". If neither happens by 11:00, the spoken text is pushed to your phone instead. The `inbox` delivery keeps the written version with links in the web UI.

3. **Know the answer format**

   The `briefing-format` skill makes the agent answer in two parts separated by one blank line:

   - First paragraph: the spoken version (the task result's `speech`). One paragraph of plain sentences, 60 to 90 seconds (about 150 to 220 words), most important first, ending with an offer for more detail.
   - Everything after: the written version (the `body`) in Markdown with sections Today, Mail, Feeds, House and weather, and Marshal, under 600 words.

   The Marshal part lists Marshal's own [health warnings](../../reference/health-endpoints.md#health-warnings), such as an MCP tool that waits for your approval or an integration that is down. `system.health` is visible only while warnings exist, so a briefing on a healthy day has no Marshal part.

4. **Ask for it on request**

   Say "good morning". The skill tells the agent not to build a new briefing in the conversation: it calls `tasks_status` with `briefing`. If the result is from today and under two hours old, it reads the prepared spoken version. Otherwise it calls `tasks_run_now`, the task prepares the briefing in the background and delivers it through its deliveries.
:::

## Tune it

- Different time: `tasks.update` the trigger, for example `expression: "30 6 * * 1-5"`.
- Different content: edit `briefing-format/SKILL.md` (changes apply without restart). Add a source by adding its tools to the template's `tools` list and a step to the skill.
- Cheaper: set the template's `modelTier` to `fast`, reduce `maxSteps`, or drop the RSS step from the skill.
- Budget: the briefing runs under the default task budget; see [`tasks`](../../configuration/marshal-yaml/tasks.md).

## Check that it works

Run `tasks_run_now briefing` ("run the briefing now"), then open **Tasks** and the task's latest result, and **Runs** to see each tool call. The same pipeline is documented in the [skills guide](../skills.md) and [agent templates](../agent-templates.md).

## Related

- [Scheduled tasks](scheduled-tasks.md), [skills](../skills.md), [agent templates](../agent-templates.md), [notifications](../notifications.md)
- [Task definitions](../../configuration/tasks-format.md)
