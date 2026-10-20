# Rate replies and replay eval cases

Rate Marshal's replies, turn rated runs into eval cases, and replay those cases against another model, template or base prompt before you switch.

## Before you start

- To rate runs, sign in to the web UI. Machine tokens cannot rate.
- To save cases and start eval runs, you need the `admin` role.
- At least one configured model per tier you want to test. The evals view lists the models from `marshal.yaml`.

## Steps

### 1. Turn on "help Marshal learn" (optional)

Open **Preferences** and switch on **Help Marshal learn**. It is off by default. While it is on, every run you rate becomes an eval case. The same switch also allows the other features that learn from your behavior; the preferences view lists them.

Ratings are stored on the run whether the switch is on or off.

### 2. Rate replies

- **In the chat:** each finished reply shows a thumbs up and a thumbs down. Thumbs down opens a note field: write what should have happened, for example "should have turned off the ceiling light, not the lamp". The note is optional.
- **In the run detail:** open **Runs**, then a run. The rating sits below the run facts. Use this to rate voice runs after the fact.

Click the current rating again to remove it. An eval case made from a rating stays when you remove the rating.

To find rated runs, filter the runs list by **Rated up** or **Rated down**.

### 3. Save runs as cases

As an admin, open a run and click **Save as eval case**. This works with the learning switch off, and for runs without a rating; such cases have the expectation `unrated` and are compared like good cases. Once a case exists, the run detail links to it.

A case stores one turn:

| Part | Content |
|---|---|
| Input | What you said or typed, with input mode, language, satellite and area |
| History | The conversation before the turn, user and assistant text only |
| Turn context | Retrieved memory, area context, time and task section as the model saw them |
| Tool calls | Tool id, arguments and the recorded result of every call |
| Reply | Marshal's reply |
| Expectation | `good` (thumbs up), `bad` (thumbs down) or `unrated` |
| Note | What should have happened |

A case is marked **incomplete** when recorded data is missing: a sensitive tool's result that was only kept as a reference, or a run recorded before Marshal stored the turn context.

### 4. Edit or delete cases

Open **System → Evals** (or **Evals** in the navigation) and click a case's date. Edit the **Expected behavior** note or delete the case.

### 5. Start an eval run

In **Evals**, select the cases and fill in **New eval run**:

- **Fast, standard and advisor tier:** the model to test per tier. **Current routing** keeps the model Marshal uses today.
- **Template:** run the cases with another [agent template](agent-templates.md).
- **Base prompt override:** replaces Marshal's base instructions for the replay.
- **Judge model:** optional. A model that scores each reply from 0 to 1 against the recorded reply and the case's note.

Click **Estimate cost** to see what the run costs before you start it, then **Start eval run**.

## How a replay works

Each case is replayed through the agent loop with the candidate configuration. History, input and turn context come from the case; the base instructions, skills and tool list come from the current configuration plus your overrides.

No tool runs in a replay:

- A call whose tool and arguments match a recorded call gets the recorded result. The order of keys in the arguments does not matter.
- Any other call gets a "no recording" error, and the case is `diverged`.

Replays write nothing to memory, tasks, notifications or integrations, and they do not appear in the runs list. Confirm-tier calls produce a confirmation question, as in an interactive turn, and nothing is queued.

## Read the report

The report shows the totals per outcome, the cost and, per case, the recorded and replayed tool calls and replies side by side with the case's note.

| Outcome | Good or unrated case | Bad case |
|---|---|---|
| `pass` | Same tools with the same arguments; same reply, or a judge score of at least 0.7 | A different tool sequence |
| `changed` | Same tools, but other arguments or another reply | Repeats the recorded tool sequence |
| `diverged` | A call had no recording | A call had no recording |
| `failed` | The replay failed (error, step limit or cost cap) | Same |

A fixed bad case usually shows as `diverged`: the right call was never recorded. Read the replayed calls next to the note to judge the fix. An extra harmless call, such as a status read, also makes a case `diverged`.

Eval runs and their results are kept until you delete them. You can cancel a queued or running eval run.

## Cost

Replays and judge calls use real models. Their cost counts against `tasks.dailyCostCap`; an eval run stops as `failed` when the cap is used up. The estimate counts the recorded text of each case at about four characters per token, sent once per model call, with the prices of the candidate models.

## Privacy

Eval cases keep the text of the rated turn (input, history, turn context, tool results and reply) beyond the run retention, until you delete them. They exist only when you rate with "help Marshal learn" on, or when an admin saves a run. Results of sensitive tools stay references. See [privacy](../concepts/privacy.md#retention).

## Check that it works

1. Rate a chat reply with thumbs down and a note.
2. As an admin, open the run and click **Save as eval case** (or check that the run links to its case).
3. In **Evals**, select the case, click **Estimate cost**, then **Start eval run**.
4. The report shows one result with the recorded and replayed calls.

## Related

- [API: evals endpoints](../api/index.md#spa-internal-endpoints)
- [Observability](observability.md) for runs and cost
- [`tasks.dailyCostCap`](../configuration/marshal-yaml/tasks.md)
