# `tasks`

Worker pool, budgets and limits of background tasks.

## Example

```yaml
tasks:
  workers: 2
  approvalTtl: "00:10:00"
  dailyCostCap: 2.0
  defaultBudget:
    cost: 0.5
    steps: 60
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `tasks.workers` | integer | `2` | Task runs that execute at the same time. Due tasks beyond it wait in order of run time. Range 1 to 16. |
| `tasks.approvalTtl` | duration | `00:10:00` | A background approval waits this long; afterwards it expires and counts as rejected. At least one minute. |
| `tasks.dailyCostCap` | decimal | `2` | Cost of all background runs of an owner per local day. No new run starts once reached. Not negative. |
| `tasks.maxDepth` | integer | `3` | Deepest child task (a top-level task has depth 0). Range 0 to 10. |
| `tasks.maxActiveChildren` | integer | `5` | Unfinished child tasks a task may have at once. Not negative. |
| `tasks.minWakeupInterval` | duration | `00:05:00` | Shortest wakeup delay and cron period. Templates can set their own. At least one minute. |
| `tasks.defaultBudget.tokens` | integer | unset (unlimited) | Input plus output tokens per task activation. |
| `tasks.defaultBudget.cost` | decimal | `0.5` | Cost per task activation. |
| `tasks.defaultBudget.steps` | integer | `60` | Model calls per task activation. |
| `tasks.defaultBudget.wallTime` | duration | `00:30:00` | Wall time per task activation. |
| `tasks.defaultBudget.runs` | integer | `20` | Runs per task activation. |

`defaultBudget` applies to tasks that do not set their own budget. An activation lasts from its trigger through its wakeups until the task is done. Child tasks draw from the parent's budget. An exhausted budget fails the task with reason `budget_exceeded`.

## Validation

- `tasks.workers must be between 1 and 16.`
- `tasks.maxDepth must be between 0 and 10.`
- `tasks.maxActiveChildren cannot be negative.`
- `tasks.minWakeupInterval must be at least one minute.`
- `tasks.dailyCostCap cannot be negative.`
- `tasks.approvalTtl must be at least one minute.`

`defaultBudget` is not validated.

## Related

- [maintenance](maintenance.md)
- [Scheduled tasks](../../guides/tasks/scheduled-tasks.md)
- [Approvals](../../guides/tasks/approvals.md)
- [Tasks format](../tasks-format.md)
