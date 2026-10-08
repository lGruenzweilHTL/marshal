<script setup lang="ts">
import { Files, Search, Trash2, Upload } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { useLiveEvent } from '../live/liveEvents'
import { AppButton, Badge, Dialog, ErrorBanner, Field, IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, ProgressBar, useDesktop, useToasts } from '../ui'
import { kindIcons, kinds, sources, type Artifact, type ArtifactKind, type ArtifactSource } from './artifactKinds'

/** The owner's artifacts, newest first: filter by kind and source, search by name, upload and delete. */
const api = useApi()
const desktop = useDesktop()
const format = useFormat()
const toasts = useToasts()
const { t } = useI18n()

const filter = reactive({ kind: '' as ArtifactKind | '', source: '' as ArtifactSource | '', query: '' })
const list = useLoad(
  () =>
    api.call((client) =>
      client.GET('/api/v1/artifacts', {
        params: { query: { kind: filter.kind || undefined, source: filter.source || undefined, query: filter.query.trim() || undefined } },
      }),
    ),
  { isEmpty: (data) => data.items.length === 0 },
)
let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(
  () => [filter.kind, filter.source],
  () => list.refresh(),
)
watch(
  () => filter.query,
  () => {
    clearTimeout(searchTimer)
    searchTimer = setTimeout(() => list.refresh(), 250)
  },
)
const filtered = computed(() => !!(filter.kind || filter.source || filter.query.trim()))

useLiveEvent('artifacts', 'artifact.created', () => list.refresh())
useLiveEvent('artifacts', 'artifact.deleted', () => list.refresh())

const fileInput = ref<HTMLInputElement | null>(null)
const progress = shallowRef<number | null>(null)
const uploadingName = shallowRef('')
const upload = useSubmit(async (file: File) => {
  uploadingName.value = file.name
  progress.value = 0
  const form = new FormData()
  form.append('file', file, file.name)
  const result = await api.upload<Artifact>('/api/v1/artifacts', form, (fraction) => (progress.value = fraction))
  progress.value = null
  if (result.data) {
    toasts.show(t('artifacts.uploaded', { name: result.data.name }))
    list.refresh()
  }
  return result
})
function picked(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (file) void upload.submit(file)
}

const removing = shallowRef<Artifact | null>(null)
const remove = useSubmit(async () => {
  const artifact = removing.value!
  const result = await api.call((client) => client.DELETE('/api/v1/artifacts/{id}', { params: { path: { id: artifact.id } } }))
  if (!result.error) {
    removing.value = null
    toasts.show(t('artifacts.deleted', { name: artifact.name }))
    list.refresh()
  }
  return result
})

const subtitle = (artifact: Artifact) =>
  [t(`artifacts.kind.${artifact.kind}`), t(`artifacts.source.${artifact.source}`), format.value.bytes(artifact.size), format.value.relative(artifact.createdAt)].join(' · ')
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('artifacts.title')" :subtitle="desktop ? t('artifacts.subtitle') : undefined">
      <template #actions>
        <AppButton variant="primary" :icon="Upload" data-test="upload" :loading="upload.busy.value" @click="fileInput?.click()">{{ t('artifacts.upload') }}</AppButton>
        <input ref="fileInput" type="file" class="sr-only" data-test="file" tabindex="-1" aria-hidden="true" @change="picked" />
      </template>
    </PageHeader>

    <div v-if="progress !== null" class="uploading" data-test="progress">
      <span class="t-body-s ink-2">{{ t('artifacts.uploading', { name: uploadingName }) }}</span>
      <ProgressBar :value="progress * 100" :label="t('artifacts.uploading', { name: uploadingName })" />
    </div>
    <ErrorBanner v-if="upload.error.value" :error="upload.error.value" />

    <div class="filters" :class="{ row: desktop }">
      <Field v-model="filter.kind" kind="select" name="kind" :label="t('artifacts.allKinds')" hide-label>
        <option value="">{{ t('artifacts.allKinds') }}</option>
        <option v-for="kind in kinds" :key="kind" :value="kind">{{ t(`artifacts.kind.${kind}`) }}</option>
      </Field>
      <Field v-model="filter.source" kind="select" name="source" :label="t('artifacts.allSources')" hide-label>
        <option value="">{{ t('artifacts.allSources') }}</option>
        <option v-for="source in sources" :key="source" :value="source">{{ t(`artifacts.source.${source}`) }}</option>
      </Field>
      <Field v-model="filter.query" kind="search" name="query" :label="t('artifacts.search')" :placeholder="t('artifacts.search')" hide-label />
    </div>

    <LoadBoundary
      :load="list"
      :empty-title="filtered ? t('artifacts.noMatch.title') : t('artifacts.empty.title')"
      :empty-body="filtered ? t('artifacts.noMatch.body') : t('artifacts.empty.body')"
      :empty-icon="filtered ? Search : Files"
    >
      <template #default="{ data }">
        <p class="t-body-s ink-2" data-test="usage">{{ t('artifacts.usage', { used: format.bytes(data.usedBytes), quota: format.bytes(data.quotaBytes) }) }}</p>
        <ListCard :label="t('artifacts.title')">
          <ListRow
            v-for="artifact in data.items"
            :key="artifact.id"
            :data-artifact="artifact.id"
            :title="artifact.name"
            :subtitle="subtitle(artifact)"
            :to="{ name: 'artifact', params: { id: artifact.id } }"
          >
            <template #lead><IconTile :icon="kindIcons[artifact.kind]" :size="36" /></template>
            <template #trail>
              <Badge v-if="artifact.tainted" tone="warning" data-test="tainted" :label="t('artifacts.tainted')" />
              <AppButton
                size="sm"
                variant="ghost"
                :icon="Trash2"
                icon-only
                :label="t('artifacts.delete')"
                data-test="delete"
                @click.prevent.stop="removing = artifact"
              />
            </template>
          </ListRow>
        </ListCard>
      </template>
    </LoadBoundary>

    <Dialog :open="!!removing" :title="t('artifacts.deleteTitle', { name: removing?.name ?? '' })" :description="t('artifacts.deleteBody')" @close="removing = null">
      <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
      <template #actions>
        <AppButton @click="removing = null">{{ t('actions.cancel') }}</AppButton>
        <AppButton variant="danger" :icon="Trash2" data-test="confirm-delete" :loading="remove.busy.value" @click="remove.submit()">{{ t('artifacts.delete') }}</AppButton>
      </template>
    </Dialog>
  </Page>
</template>

<style scoped>
.uploading {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.filters {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.filters.row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 2fr);
}
</style>
