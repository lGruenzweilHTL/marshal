<script setup lang="ts">
import { Download, ExternalLink } from 'lucide-vue-next'
import { computed, defineAsyncComponent, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { renderMarkdown } from '../chat/markdown'
import { useFormat } from '../i18n/locale'
import DiffView from '../inbox/DiffView.vue'
import { AppButton, Badge, Banner, Card, KeyValueList, LoadBoundary, Page, PageHeader, Spinner } from '../ui'
import type { Artifact } from './artifactKinds'

// three.js and highlight.js load only when an artifact needs them.
const CodeViewer = defineAsyncComponent(() => import('./viewers/CodeViewer.vue'))
const ModelViewer = defineAsyncComponent(() => import('./viewers/ModelViewer.vue'))

/**
 * One artifact in the viewer for its kind. Content always comes from Marshal's origin: images and SVG through
 * `<img>` (scripts in SVG never run), HTML in a sandboxed iframe without scripts, PDF in a new tab, `other` as a download.
 */
const api = useApi()
const route = useRoute()
const format = useFormat()
const { t } = useI18n()
const id = computed(() => String(route.params.id))
const artifact = useLoad(() => api.call((client) => client.GET('/api/v1/artifacts/{id}', { params: { path: { id: id.value } } })))
const contentPath = computed(() => `/api/v1/artifacts/${encodeURIComponent(id.value)}/content`)
const contentUrl = computed(() => `${api.baseUrl}${contentPath.value}`)

const textKinds = new Set(['code', 'diff', 'markdown'])
const text = shallowRef<string | null>(null)
watch(
  () => artifact.data.value,
  async (loaded) => {
    text.value = null
    if (!loaded || !textKinds.has(loaded.kind)) return
    const result = await api.fetch(contentPath.value)
    if (result.data?.ok) text.value = await result.data.text()
  },
  { immediate: true },
)

const details = (item: Artifact) => [
  { key: 'source', label: t('artifacts.from'), value: item.sourceReference ? `${t(`artifacts.source.${item.source}`)} · ${item.sourceReference}` : t(`artifacts.source.${item.source}`) },
  { key: 'size', label: t('artifacts.size'), value: format.value.bytes(item.size) },
  { key: 'stored', label: t('artifacts.stored'), value: format.value.dateTime(item.createdAt) },
]
</script>

<template>
  <Page width="wide">
    <LoadBoundary :load="artifact" size="page">
      <template #default="{ data }">
        <PageHeader :title="data.name" :back="{ name: 'artifacts' }" :back-label="t('artifacts.back')">
          <template #subtitle>
            <span class="markers">
              <Badge data-test="kind" :label="t(`artifacts.kind.${data.kind}`)" />
              <Badge tone="info" data-test="source" :label="t(`artifacts.source.${data.source}`)" />
              <Badge v-if="data.tainted" tone="warning" data-test="tainted" :label="t('artifacts.tainted')" />
            </span>
          </template>
          <template #actions>
            <AppButton :icon="Download" :href="contentUrl" :download="data.name" data-test="download">{{ t('artifacts.download') }}</AppButton>
          </template>
        </PageHeader>
        <Banner v-if="data.tainted" tone="warning" :title="t('artifacts.tainted')" :body="t('artifacts.taintedHint')" />

        <div class="viewer" :data-kind="data.kind">
          <img v-if="data.kind === 'image' || data.kind === 'svg'" class="media" :src="contentUrl" :alt="data.name" data-test="image" />
          <video v-else-if="data.kind === 'video'" class="media" :src="contentUrl" controls preload="metadata" data-test="video"></video>
          <ModelViewer v-else-if="data.kind === 'model3d'" :id="data.id" :name="data.name" :size="data.size" />
          <iframe v-else-if="data.kind === 'html'" class="frame" :src="contentUrl" sandbox="" referrerpolicy="no-referrer" :title="data.name" data-test="html"></iframe>
          <Card v-else-if="data.kind === 'pdf'">
            <p class="ink-2">{{ t('artifacts.openPdfHint') }}</p>
            <AppButton :icon="ExternalLink" :href="contentUrl" target="_blank" rel="noopener noreferrer" data-test="pdf">{{ t('artifacts.openPdf') }}</AppButton>
          </Card>
          <template v-else-if="textKinds.has(data.kind)">
            <Spinner v-if="text === null" />
            <CodeViewer v-else-if="data.kind === 'code'" :name="data.name" :text="text" />
            <DiffView v-else-if="data.kind === 'diff'" :diff="text" />
            <!-- eslint-disable-next-line vue/no-v-html -- rendered with HTML disabled and same-origin images only -->
            <Card v-else><div class="markdown" data-test="markdown" v-html="renderMarkdown(text)"></div></Card>
          </template>
          <Card v-else data-test="no-preview"><p class="ink-2">{{ t('artifacts.noPreview') }}</p></Card>
        </div>

        <KeyValueList :items="details(data)" />
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.markers {
  display: inline-flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.media {
  display: block;
  max-width: 100%;
  max-height: 75vh;
  border-radius: var(--radius-md);
  background: var(--surface-2);
}
.frame {
  width: 100%;
  height: 70vh;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  /* Pages render on the browser's light canvas, as they would standalone, in both themes. */
  color-scheme: light;
  background: Canvas;
}
.markdown :deep(a) {
  color: var(--accent-text);
}
</style>
