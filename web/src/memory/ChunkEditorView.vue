<script setup lang="ts">
import { Check, History, Link2, Pencil, Plus, Replace, Trash2 } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import {
  AppButton,
  Badge,
  Card,
  Dialog,
  ErrorBanner,
  Field,
  IconTile,
  ListCard,
  ListRow,
  LoadBoundary,
  Page,
  PageHeader,
  Section,
  Segmented,
  Switch,
  useDesktop,
} from '../ui'
import ChunkType from './ChunkType.vue'
import { chunkTypes } from './memoryLabels'

type Draft = { scope: Schemas['ChunkScope']; projectId: string; type: Schemas['ChunkType']; title: string; content: string; importance: number; sourceRef: string }
type Importance = '1' | '2' | '3' | '4' | '5'

/**
 * A memory chunk: create, edit content and importance, pin, supersede (new identity for a new title, type or
 * scope) and delete, with its history and links. Failed saves keep the text.
 */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()
const id = computed(() => (typeof route.params.id === 'string' ? route.params.id : null))

const projects = useLoad(() => api.call((client) => client.GET('/api/v1/memory/projects')), { isEmpty: () => false })
const path = () => ({ params: { path: { id: id.value! } } })
const detail = useLoad<Schemas['MemoryChunkDetail'] | null>(
  async () => {
    if (!id.value) return { data: null }
    return api.call((client) => client.GET('/api/v1/memory/chunks/{id}', path()))
  },
  { isEmpty: () => false },
)
const history = useLoad<Schemas['MemoryHistoryEntry'][]>(
  async () => {
    if (!id.value) return { data: [] as Schemas['MemoryHistoryEntry'][] }
    return api.call((client) => client.GET('/api/v1/memory/chunks/{id}/history', path()))
  },
  { isEmpty: () => false },
)

const emptyDraft = (): Draft => ({ scope: 'general', projectId: '', type: 'fact', title: '', content: '', importance: 3, sourceRef: '' })
const form = reactive<Draft>(emptyDraft())
const replacement = reactive<Draft>(emptyDraft())
const superseding = shallowRef(false)
const deleting = shallowRef(false)
const saved = shallowRef<string | null>(null)
const chunk = computed(() => detail.data.value?.chunk ?? null)
const deleted = computed(() => chunk.value?.deletedAt != null)

watch(chunk, (value) => {
  if (!value) return
  Object.assign(form, { scope: value.scope, projectId: value.projectId ?? '', type: value.type, title: value.title, content: value.content, importance: value.importance, sourceRef: value.sourceRef ?? '' })
  Object.assign(replacement, form)
})
watch(id, () => {
  saved.value = null
  superseding.value = false
  if (!id.value) Object.assign(form, emptyDraft())
  detail.retry()
  history.retry()
})

const importance = (draft: Draft) =>
  computed<Importance>({ get: () => String(draft.importance) as Importance, set: (value) => (draft.importance = Number(value)) })
const formImportance = importance(form)
const replacementImportance = importance(replacement)
const levels = (['1', '2', '3', '4', '5'] as const).map((value) => ({ value, label: value }))

const body = (draft: Draft): Schemas['ChunkWriteRequest'] => ({
  scope: draft.scope,
  projectId: draft.scope === 'project' ? draft.projectId : null,
  type: draft.type,
  title: draft.title,
  content: draft.content,
  importance: draft.importance,
  sourceRef: draft.sourceRef.trim() || null,
})

const save = useSubmit(async () => {
  saved.value = null
  if (!id.value) {
    const result = await api.call((client) => client.POST('/api/v1/memory/chunks', { body: body(form) }))
    if (result.data) await router.push(`/memory/chunks/${result.data.id}`)
    return result
  }
  const result = await api.call((client) =>
    client.PUT('/api/v1/memory/chunks/{id}', { ...path(), body: { content: form.content, importance: form.importance, sourceRef: form.sourceRef.trim() || null } }),
  )
  if (result.data) {
    saved.value = result.data.outcome === 'unchanged' ? t('memory.editor.unchanged') : t('states.saved')
    detail.refresh()
    history.refresh()
  }
  return result
})

const pin = useSubmit(async (pinned: boolean) => {
  const result = await api.call((client) => (pinned ? client.PUT('/api/v1/memory/chunks/{id}/pin', path()) : client.DELETE('/api/v1/memory/chunks/{id}/pin', path())))
  detail.refresh()
  return result
})
const pinned = computed({ get: () => chunk.value?.pinned ?? false, set: (value: boolean) => void pin.submit(value) })

const supersede = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/memory/chunks/{id}/supersede', { ...path(), body: body(replacement) }))
  if (result.data) await router.push(`/memory/chunks/${result.data.id}`)
  return result
})

const remove = useSubmit(async () => {
  const result = await api.call((client) => client.DELETE('/api/v1/memory/chunks/{id}', path()))
  if (!result.error) await router.push('/memory')
  return result
})

const projectOptions = computed(() => projects.data.value ?? [])
</script>

<template>
  <Page width="wide">
    <LoadBoundary :load="detail" size="page">
      <PageHeader :title="id ? (chunk?.title ?? t('memory.editor.chunkTitle')) : t('memory.editor.newTitle')" back="/memory" :back-label="t('memory.title')">
        <template v-if="chunk && !deleted" #actions>
          <AppButton variant="ghost" :icon="Trash2" data-test="delete" @click="deleting = true">{{ t('memory.editor.delete') }}</AppButton>
        </template>
      </PageHeader>
      <p v-if="chunk" class="meta">
        <ChunkType :type="chunk.type" />
        <span>{{ t('memory.editor.meta', { who: chunk.createdBy, created: format.dateTime(chunk.createdAt) }) }}</span>
        <span v-if="chunk.lastRetrievedAt">· {{ t('memory.editor.lastUsed', { time: format.dateTime(chunk.lastRetrievedAt) }) }}</span>
        <Badge v-if="deleted" tone="danger" :label="t('memory.editor.deleted')" />
      </p>

      <div class="layout" :class="{ split: desktop && !!chunk }">
        <div class="main">
          <Card>
            <form class="form" data-test="chunk" @submit.prevent="save.submit()">
              <div class="pair">
                <Field v-model="form.scope" kind="select" name="scope" :label="t('memory.editor.scope')" :disabled="!!id">
                  <option value="general">{{ t('memory.editor.scopes.general') }}</option>
                  <option value="project">{{ t('memory.editor.scopes.project') }}</option>
                </Field>
                <Field v-if="form.scope === 'project'" v-model="form.projectId" kind="select" name="projectId" :label="t('memory.editor.project')" :disabled="!!id" required>
                  <option v-for="project in projectOptions" :key="project.id" :value="project.id">{{ project.name }}</option>
                </Field>
                <Field v-model="form.type" kind="select" name="type" :label="t('memory.editor.type')" :disabled="!!id">
                  <option v-for="type in chunkTypes" :key="type" :value="type">{{ t(`memory.type.${type}`) }}</option>
                </Field>
              </div>
              <Field v-model="form.title" name="title" :label="t('memory.editor.titleField')" :disabled="!!id" required />
              <Field v-model="form.content" kind="textarea" name="content" :rows="5" :label="t('memory.editor.content')" :hint="t('memory.editor.contentHint')" required />
              <div class="importance">
                <span class="t-label">{{ t('memory.editor.importance') }}</span>
                <Segmented v-model="formImportance" :options="levels" :label="t('memory.editor.importance')" block data-test="importance" />
              </div>
              <Field v-model="form.sourceRef" name="sourceRef" :label="t('memory.editor.source')" :placeholder="t('memory.editor.sourceHint')" mono />
              <Switch v-if="chunk && !deleted" v-model="pinned" :label="t('memory.editor.pin')" :description="t('memory.editor.pinHint')" :disabled="pin.busy.value" data-test="pin" />
              <ErrorBanner v-if="pin.error.value" :error="pin.error.value" />
              <ErrorBanner v-if="save.error.value" :error="save.error.value" />
              <p v-if="id" class="hint">{{ t('memory.editor.locked') }}</p>
              <div class="actions">
                <AppButton type="submit" variant="primary" :icon="id ? Check : Plus" :loading="save.busy.value">{{ id ? t('memory.editor.save') : t('memory.editor.create') }}</AppButton>
                <AppButton v-if="chunk && !deleted" :icon="Replace" data-test="show-supersede" @click="superseding = !superseding">{{ t('memory.editor.supersede') }}</AppButton>
                <span v-if="saved" role="status" class="saved">{{ saved }}</span>
              </div>
            </form>
          </Card>

          <Card v-if="superseding" :title="t('memory.editor.replacement')">
            <form class="form" data-test="supersede" @submit.prevent="supersede.submit()">
              <div class="pair">
                <Field v-model="replacement.scope" kind="select" name="newScope" :label="t('memory.editor.scope')">
                  <option value="general">{{ t('memory.editor.scopes.general') }}</option>
                  <option value="project">{{ t('memory.editor.scopes.project') }}</option>
                </Field>
                <Field v-if="replacement.scope === 'project'" v-model="replacement.projectId" kind="select" name="newProjectId" :label="t('memory.editor.project')" required>
                  <option v-for="project in projectOptions" :key="project.id" :value="project.id">{{ project.name }}</option>
                </Field>
                <Field v-model="replacement.type" kind="select" name="newType" :label="t('memory.editor.type')">
                  <option v-for="type in chunkTypes" :key="type" :value="type">{{ t(`memory.type.${type}`) }}</option>
                </Field>
              </div>
              <Field v-model="replacement.title" name="newTitle" :label="t('memory.editor.titleField')" required />
              <Field v-model="replacement.content" kind="textarea" name="newContent" :rows="5" :label="t('memory.editor.content')" required />
              <div class="importance">
                <span class="t-label">{{ t('memory.editor.importance') }}</span>
                <Segmented v-model="replacementImportance" :options="levels" :label="t('memory.editor.importance')" block />
              </div>
              <ErrorBanner v-if="supersede.error.value" :error="supersede.error.value" />
              <div class="actions">
                <AppButton type="submit" variant="primary" :icon="Replace" :loading="supersede.busy.value">{{ t('memory.editor.replace') }}</AppButton>
              </div>
            </form>
          </Card>
        </div>

        <div v-if="chunk" class="side">
          <Section :title="t('memory.editor.history')" :count="history.data.value?.length">
            <LoadBoundary :load="history">
              <ListCard>
                <ListRow
                  v-for="entry in history.data.value ?? []"
                  :key="`${entry.kind}/${entry.chunkId}/${entry.validFrom}`"
                  :data-history="entry.kind"
                  :chevron="false"
                >
                  <template #lead><IconTile :icon="entry.kind === 'current' ? Pencil : History" /></template>
                  <template #title>
                    <RouterLink v-if="entry.chunkId !== id" :to="`/memory/chunks/${entry.chunkId}`">{{ entry.title }}</RouterLink>
                    <template v-else>{{ t(`memory.editor.historyKind.${entry.kind}`) }}</template>
                  </template>
                  <template #subtitle>{{ entry.content }} · <span class="num">{{ format.dateTime(entry.validFrom) }}</span></template>
                  <template #trail>
                    <Badge :tone="entry.kind === 'current' ? 'success' : 'neutral'" :label="t(`memory.editor.historyKind.${entry.kind}`)" />
                  </template>
                </ListRow>
              </ListCard>
            </LoadBoundary>
          </Section>
          <Section v-if="detail.data.value?.edges.length" :title="t('memory.editor.links')">
            <ListCard>
              <ListRow
                v-for="edge in detail.data.value.edges"
                :key="`${edge.from}/${edge.kind}/${edge.to}`"
                data-test="edge"
                :to="`/memory/chunks/${edge.from === id ? edge.to : edge.from}`"
                :title="edge.from === id ? edge.to : edge.from"
                :subtitle="edge.from === id ? t(`memory.editor.edge.${edge.kind}`) : t('memory.editor.incoming', { kind: t(`memory.editor.edge.${edge.kind}`) })"
              >
                <template #lead><IconTile :icon="Link2" /></template>
              </ListRow>
            </ListCard>
          </Section>
        </div>
      </div>

      <Dialog
        :open="deleting"
        :title="t('memory.editor.deleteTitle')"
        :description="t('memory.editor.deleteBody', { title: chunk?.title ?? '' })"
        @close="deleting = false"
      >
        <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
        <template #actions>
          <AppButton @click="deleting = false">{{ t('actions.cancel') }}</AppButton>
          <AppButton variant="danger" :icon="Trash2" data-test="confirm-delete" :loading="remove.busy.value" @click="remove.submit()">{{ t('memory.editor.delete') }}</AppButton>
        </template>
      </Dialog>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  margin-top: calc(-1 * var(--space-4));
  color: var(--ink-3);
  font: var(--text-body-s);
}
.layout,
.main,
.side {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 380px;
  gap: 28px;
  align-items: start;
}
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.pair {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--space-4);
}
.importance {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.hint {
  color: var(--ink-3);
  font: var(--text-body-s);
}
.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.saved {
  color: var(--success);
  font: 600 14px/20px var(--font-sans);
}
</style>
