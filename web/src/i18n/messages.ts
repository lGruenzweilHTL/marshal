import type { Catalog } from './catalog'
import * as common from './common'
import * as chat from '../chat/messages'
import * as home from '../home/messages'
import * as inbox from '../inbox/messages'
import * as memory from '../memory/messages'
import * as shell from '../layout/messages'
import * as printers from '../printers/messages'
import * as runs from '../runs/messages'
import * as tasks from '../tasks/messages'

/**
 * All catalogs merged: shared texts at the root, each area under its own key. English is the source; the German
 * catalog is typed against it, so a missing German key fails `vue-tsc`.
 */
export const en = {
  ...common.en,
  shell: shell.en,
  inbox: inbox.en,
  printers: printers.en,
  home: home.en,
  runs: runs.en,
  tasks: tasks.en,
  chat: chat.en,
  memory: memory.en,
}

export const de: Catalog<typeof en> = {
  ...common.de,
  shell: shell.de,
  inbox: inbox.de,
  printers: printers.de,
  home: home.de,
  runs: runs.de,
  tasks: tasks.de,
  chat: chat.de,
  memory: memory.de,
}

export type MessageSchema = typeof en
