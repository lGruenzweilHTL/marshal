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

/** A .NET time span (`[d.]hh:mm:ss`) as hours and minutes, e.g. `3 h 30 min`. */
export function spanLabel(span: string): string {
  const match = /^(?:(\d+)\.)?(\d+):(\d+)/.exec(span)
  if (!match) return span
  const hours = Number(match[1] ?? 0) * 24 + Number(match[2])
  const minutes = Number(match[3])
  return [hours ? `${hours} h` : '', minutes ? `${minutes} min` : ''].filter(Boolean).join(' ') || '0 min'
}
