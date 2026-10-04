import type { Catalog } from './catalog'
import * as common from './common'
import * as shell from '../layout/messages'

/**
 * All catalogs merged: shared texts at the root, each area under its own key. English is the source; the German
 * catalog is typed against it, so a missing German key fails `vue-tsc`.
 */
export const en = {
  ...common.en,
  shell: shell.en,
}

export const de: Catalog<typeof en> = {
  ...common.de,
  shell: shell.de,
}

export type MessageSchema = typeof en
