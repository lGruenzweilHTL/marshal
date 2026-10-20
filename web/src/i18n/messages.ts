import type { Catalog } from './catalog'
import * as common from './common'
import * as artifacts from '../artifacts/messages'
import * as auth from '../auth/messages'
import * as chat from '../chat/messages'
import * as evals from '../evals/messages'
import * as home from '../home/messages'
import * as inbox from '../inbox/messages'
import * as mail from '../mail/messages'
import * as memory from '../memory/messages'
import * as shell from '../layout/messages'
import * as preferences from '../preferences/messages'
import * as printers from '../printers/messages'
import * as runs from '../runs/messages'
import * as security from '../security/messages'
import * as system from '../system/messages'
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
  artifacts: artifacts.en,
  chat: chat.en,
  memory: memory.en,
  mail: mail.en,
  preferences: preferences.en,
  security: security.en,
  system: system.en,
  evals: evals.en,
  auth: auth.en,
}

export const de: Catalog<typeof en> = {
  ...common.de,
  shell: shell.de,
  inbox: inbox.de,
  printers: printers.de,
  home: home.de,
  runs: runs.de,
  tasks: tasks.de,
  artifacts: artifacts.de,
  chat: chat.de,
  memory: memory.de,
  mail: mail.de,
  preferences: preferences.de,
  security: security.de,
  system: system.de,
  evals: evals.de,
  auth: auth.de,
}

export type MessageSchema = typeof en
