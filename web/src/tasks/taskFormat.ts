import type { Schemas } from '../api/client'

const unfinished: Schemas['AgentTaskStatus'][] = ['pending', 'running', 'waiting']

/** Pending, running or waiting: the task can still run, so it can be cancelled. */
export function isUnfinished(status: Schemas['AgentTaskStatus']): boolean {
  return unfinished.includes(status)
}

/** The task's name, or its goal cut to a line. */
export function taskLabel(task: Pick<Schemas['TaskItem'], 'name' | 'goal'>): string {
  if (task.name) return task.name
  return task.goal.length > 80 ? `${task.goal.slice(0, 80)}…` : task.goal
}
