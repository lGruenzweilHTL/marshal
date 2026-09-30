import type { Schemas } from '../api/client'
import { formatCost } from '../format'

const unfinished: Schemas['AgentTaskStatus'][] = ['pending', 'running', 'waiting']

/** Pending, running or waiting: the task can still run, so it can be cancelled. */
export function isUnfinished(status: Schemas['AgentTaskStatus']): boolean {
  return unfinished.includes(status)
}

/** A trigger as the task tools describe it; watches (event triggers) read as `watch`. */
export function describeTrigger(trigger: Schemas['TaskTrigger']): string {
  switch (trigger.kind) {
    case 'now':
      return 'now'
    case 'event':
      return `watch ${trigger.spec}`
    default:
      return `${trigger.kind} ${trigger.spec}`
  }
}

/** Cost spent in this activation, with the task's cost budget when it has one. */
export function budgetUse(task: Schemas['TaskItem']): string {
  const spent = formatCost(task.spent.cost)
  return task.budget.cost === null ? spent : `${spent} / ${formatCost(task.budget.cost)}`
}

/** The task's name, or its goal cut to a line. */
export function taskLabel(task: Pick<Schemas['TaskItem'], 'name' | 'goal'>): string {
  if (task.name) return task.name
  return task.goal.length > 80 ? `${task.goal.slice(0, 80)}…` : task.goal
}
