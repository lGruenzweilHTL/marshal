# Turn repeated workflows into skills

Let Marshal notice requests you repeat, draft a skill for them, and add the skills you want to `/config/skills`.

## Before you start

- Sign in to the web UI.
- You need write access to `/config/skills` on the host. Marshal never writes to `/config` itself.
- Read [skills](skills.md) to know what a skill is and how Marshal loads it.

## Steps

::: steps
1. **Turn on "help Marshal learn"**

   Open **Preferences** and switch on **Help Marshal learn**. The nightly job runs only for owners with the switch on.

2. **Let the nightly job find workflows**

   Every night at 04:00 (`maintenance.skillProposalSchedule`), Marshal looks at your runs of the last 30 days. A workflow is a group of at least three runs that:

   - finished `ok` and called at least three tools,
   - were not rated down,
   - called the same tools in the same order,
   - started from similar requests.

   A `standard` tier model drafts a `SKILL.md` for each workflow. It sees your requests and the tool and argument names, never tool results or argument values. When an existing skill already covers the workflow, nothing is drafted. A workflow you dismissed is never proposed again.

   When there are new drafts, one `low` notification summarizes them.

3. **Review a proposal**

   Open the **Inbox**. Skill proposals are listed under **Skill proposals**. Open one to see:

   - the drafted `SKILL.md`,
   - the diff against an existing skill with the same name, when there is one,
   - links to the runs the workflow came from.

4. **Add the skill or dismiss it**

   - Click **Download SKILL.md**. Save the file as `/config/skills/<name>/SKILL.md`, using the proposal's name. Edit it first if you want. Marshal loads it when the folder changes.
   - Click **Dismiss** to drop the proposal. The same workflow is not proposed again.
:::

## Check that it works

After you saved the file, open **System**, then the configuration view, and check that the skill is not listed as rejected. Ask for the workflow again; the run detail shows an `agent_load_skill` tool step.

## Related

- [Skills](skills.md)
- [Skills format](../configuration/skills-format.md)
- [`maintenance` configuration](../configuration/marshal-yaml/maintenance.md)
- [Get suggestions](suggestions.md)
