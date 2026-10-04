<script setup lang="ts">
import { Check, FolderClosed, History, Pencil, Plus, Trash2, X } from 'lucide-vue-next'
import { reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import type { ApiResult } from '../api/errors'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Dialog, ErrorBanner, Field, IconTile } from '../ui'

/** One project: name, description, aliases (candidates to accept or reject), the correction log, edit and delete. */
const props = defineProps<{ project: Schemas['ProjectView'] }>()
const emit = defineEmits<{ changed: [] }>()
const api = useApi()
const format = useFormat()
const { t } = useI18n()

const edit = reactive({ name: props.project.name, description: props.project.description })
watch(
  () => props.project,
  (project) => Object.assign(edit, { name: project.name, description: project.description }),
)
const editing = shallowRef(false)
const deleting = shallowRef(false)
const alias = shallowRef('')
const feedback = shallowRef<Schemas['ProjectFeedbackView'][] | null>(null)
const path = () => ({ params: { path: { id: props.project.id } } })
const aliasPath = (aliasId: string) => ({ params: { path: { id: props.project.id, aliasId } } })

function changed<T>(result: ApiResult<T>): ApiResult<T> {
  if (!result.error) emit('changed')
  return result
}

const save = useSubmit(async () => {
  const result = await api.call((client) => client.PUT('/api/v1/memory/projects/{id}', { ...path(), body: { name: edit.name, description: edit.description } }))
  if (result.data) editing.value = false
  return changed(result)
})
const remove = useSubmit(async () => changed(await api.call((client) => client.DELETE('/api/v1/memory/projects/{id}', path()))))
const addAlias = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/memory/projects/{id}/aliases', { ...path(), body: { phrase: alias.value } }))
  if (!result.error) alias.value = ''
  return changed(result)
})
const aliasAction = useSubmit(async (aliasId: string, status: Schemas['ProjectAliasStatus'] | null) =>
  changed(
    await api.call((client) =>
      status ? client.PUT('/api/v1/memory/projects/{id}/aliases/{aliasId}', { ...aliasPath(aliasId), body: { status } }) : client.DELETE('/api/v1/memory/projects/{id}/aliases/{aliasId}', aliasPath(aliasId)),
    ),
  ),
)
const corrections = useSubmit(async () => {
  const result = await api.call((client) => client.GET('/api/v1/memory/projects/{id}/feedback', path()))
  if (result.data) feedback.value = result.data
  return result
})

function toggleFeedback() {
  if (feedback.value) feedback.value = null
  else void corrections.submit()
}
const aliasLabel = (entry: Schemas['ProjectAliasView']) =>
  entry.status === 'candidate' ? t('memory.projects.candidate', entry.confirmations) : t(`memory.projects.status.${entry.status}`)
</script>

<template>
  <article class="project" :data-project="project.id">
    <div class="head">
      <IconTile :icon="FolderClosed" :size="40" />
      <div class="names">
        <h2 class="name">{{ project.name }}</h2>
        <p v-if="project.description" class="description">{{ project.description }}</p>
      </div>
      <AppButton size="sm" variant="ghost" :icon="Pencil" icon-only :label="t('memory.projects.edit')" data-test="edit" @click="editing = !editing" />
      <AppButton size="sm" variant="ghost" :icon="Trash2" icon-only :label="t('memory.projects.delete')" data-test="delete" @click="deleting = true" />
    </div>
    <p class="facts">
      <span class="num">{{ t('memory.projects.chunks', project.chunkCount) }}</span>
      <template v-if="project.lastActiveAt"> · {{ t('memory.projects.lastActive', { time: format.dateTime(project.lastActiveAt) }) }}</template>
      · <RouterLink :to="{ path: '/memory', query: { project: project.id } }">{{ t('memory.projects.showMemory') }}</RouterLink>
    </p>

    <form v-if="editing" class="form" data-test="edit-project" @submit.prevent="save.submit()">
      <Field v-model="edit.name" name="name" :label="t('memory.projects.name')" required />
      <Field v-model="edit.description" kind="textarea" :rows="2" name="description" :label="t('memory.projects.description')" />
      <ErrorBanner v-if="save.error.value" :error="save.error.value" />
      <div class="buttons">
        <AppButton type="submit" size="sm" variant="primary" :icon="Check" :loading="save.busy.value">{{ t('memory.projects.save') }}</AppButton>
        <AppButton size="sm" variant="ghost" @click="editing = false">{{ t('memory.projects.cancel') }}</AppButton>
      </div>
    </form>

    <div class="aliases">
      <span class="t-overline">{{ t('memory.projects.aliases') }}</span>
      <ul class="chips">
        <li v-for="entry in project.aliases" :key="entry.id" class="alias" :class="entry.status" :data-alias="entry.phrase">
          <span>“{{ entry.phrase }}”</span>
          <span class="status">{{ aliasLabel(entry) }}</span>
          <button v-if="entry.status !== 'active'" type="button" class="round accept" data-test="promote" :aria-label="t('memory.projects.accept')" @click="aliasAction.submit(entry.id, 'active')">
            <Check :size="14" :stroke-width="2.5" />
          </button>
          <button v-if="entry.status !== 'rejected'" type="button" class="round" data-test="reject" :aria-label="t('memory.projects.reject')" @click="aliasAction.submit(entry.id, 'rejected')">
            <X :size="14" :stroke-width="2.5" />
          </button>
          <button v-else type="button" class="round" data-test="remove-alias" :aria-label="t('memory.projects.remove')" @click="aliasAction.submit(entry.id, null)">
            <Trash2 :size="14" />
          </button>
        </li>
      </ul>
      <p v-if="project.aliases.some((entry) => entry.status === 'candidate')" class="hint">{{ t('memory.projects.aliasHint') }}</p>
      <ErrorBanner v-if="aliasAction.error.value" :error="aliasAction.error.value" />
      <form class="add" data-test="add-alias" @submit.prevent="addAlias.submit()">
        <Field v-model="alias" name="alias" :label="t('memory.projects.newAlias')" hide-label :placeholder="t('memory.projects.newAlias')" required />
        <AppButton type="submit" :icon="Plus" :loading="addAlias.busy.value">{{ t('memory.projects.addAlias') }}</AppButton>
      </form>
      <ErrorBanner v-if="addAlias.error.value" :error="addAlias.error.value" />
    </div>

    <div class="corrections">
      <AppButton size="sm" variant="ghost" :icon="History" data-test="feedback" :loading="corrections.busy.value" @click="toggleFeedback">
        {{ feedback ? t('memory.projects.hideCorrections') : t('memory.projects.showCorrections') }}
      </AppButton>
      <ErrorBanner v-if="corrections.error.value" :error="corrections.error.value" />
      <ul v-if="feedback" class="log" data-test="feedback-log">
        <li v-if="!feedback.length" class="ink-3">{{ t('memory.projects.noCorrections') }}</li>
        <li v-for="entry in feedback" :key="entry.id">
          <span class="num ink-3">{{ format.dateTime(entry.createdAt) }}</span> · {{ entry.kind }} · “{{ entry.phrase }}”
          <RouterLink v-if="entry.runId" :to="`/runs/${entry.runId}`">{{ t('memory.projects.run') }}</RouterLink>
        </li>
      </ul>
    </div>

    <Dialog :open="deleting" :title="t('memory.projects.deleteTitle', { name: project.name })" :description="t('memory.projects.deleteBody')" @close="deleting = false">
      <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
      <template #actions>
        <AppButton @click="deleting = false">{{ t('actions.cancel') }}</AppButton>
        <AppButton variant="danger" :icon="Trash2" data-test="confirm-delete" :loading="remove.busy.value" @click="remove.submit()">{{ t('memory.projects.delete') }}</AppButton>
      </template>
    </Dialog>
  </article>
</template>

<style scoped>
.project {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
  overflow-wrap: anywhere;
}
.head {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
}
.names {
  flex-grow: 1;
  min-width: 0;
}
.name {
  font: 620 18px/24px var(--font-display);
}
.description,
.facts {
  color: var(--ink-2);
  font: var(--text-body-s);
}
.form,
.aliases,
.corrections {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.buttons,
.add {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-2);
}
.add > :first-child {
  flex: 1 1 200px;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}
.alias {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 32px;
  padding: 0 4px 0 12px;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  font: 400 13px/18px var(--font-sans);
}
.alias.candidate {
  border: 1px dashed var(--line-strong);
  background: transparent;
}
.alias.rejected {
  color: var(--ink-3);
  text-decoration: line-through;
}
.status {
  color: var(--ink-3);
  font-size: 12px;
}
.alias.active {
  padding-right: 4px;
}
.round {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: 0;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  color: var(--ink-2);
  cursor: pointer;
}
.alias.candidate .round,
.alias.active .round {
  background: var(--surface-2);
}
.round.accept {
  background: var(--success-soft);
  color: var(--success);
}
.round:focus-visible {
  outline: var(--focus-ring);
}
.hint {
  color: var(--ink-3);
  font: var(--text-body-s);
}
.log {
  margin: 0;
  padding-left: var(--space-5);
  font: var(--text-body-s);
}
</style>
