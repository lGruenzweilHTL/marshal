<script setup lang="ts">
import { Download, X } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { useInboxCountOptional } from '../layout/inboxCount'
import { AppButton, Badge, Card, CodeBlock, ErrorBanner, LoadBoundary, Page, PageHeader, useToasts, type Tone } from '../ui'
import DiffView from './DiffView.vue'

/**
 * A skill proposal: the drafted SKILL.md, the diff against a same-name skill, and the runs it came from. Download
 * hands the file to the user; Marshal never writes to /config itself.
 */
const api = useApi()
const inbox = useInboxCountOptional()
const route = useRoute()
const format = useFormat()
const toasts = useToasts()
const { t } = useI18n()
const id = String(route.params.id)

const proposal = useLoad(() => api.call((client) => client.GET('/api/v1/skill-proposals/{id}', { params: { path: { id } } })))
const downloaded = shallowRef(false)
const dismissedNow = shallowRef(false)
const status = computed(() => (dismissedNow.value ? 'dismissed' : downloaded.value ? 'downloaded' : proposal.data.value?.status) ?? 'open')
const tones: Record<string, Tone> = { open: 'info', downloaded: 'success', dismissed: 'neutral', covered: 'neutral' }
const downloadUrl = `${api.baseUrl}/api/v1/skill-proposals/${encodeURIComponent(id)}/download`
const runs = computed(() => (proposal.data.value?.sourceRuns ?? []).map(String))

const dismiss = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/skill-proposals/{id}/dismiss', { params: { path: { id } } }))
  if (!result.error) {
    dismissedNow.value = true
    toasts.show(t('inbox.skill.dismissed'))
    void inbox?.refresh()
  }
  return result
})

function markDownloaded() {
  downloaded.value = true
  void inbox?.refresh()
}
</script>

<template>
  <Page width="wide">
    <LoadBoundary :load="proposal" size="page">
      <template #default="{ data }">
        <PageHeader :title="data.name" :subtitle="t('inbox.skill.intro')" back="/inbox" :back-label="t('inbox.title')">
          <template v-if="status === 'open' || status === 'downloaded'" #actions>
            <AppButton v-if="status === 'open'" :icon="X" data-test="dismiss" :loading="dismiss.busy.value" @click="dismiss.submit()">{{ t('inbox.skill.dismiss') }}</AppButton>
            <AppButton variant="primary" :icon="Download" :href="downloadUrl" download="SKILL.md" data-test="download" @click="markDownloaded">
              {{ t('inbox.skill.download') }}
            </AppButton>
          </template>
        </PageHeader>
        <div class="meta">
          <Badge :tone="tones[status]" :label="t(`inbox.skill.status.${status}`)" />
          <span>{{ data.description }}</span>
          <span class="facts">
            {{ t('inbox.skill.sources', runs.length) }}:
            <template v-for="(run, index) in runs" :key="run"><template v-if="index">, </template><RouterLink :to="`/runs/${run}`" class="num">{{ index + 1 }}</RouterLink></template>
            · <span class="num">{{ format.dateTime(data.createdAt) }}</span>
          </span>
        </div>
        <ErrorBanner v-if="dismiss.error.value" :error="dismiss.error.value" />
        <p v-if="status === 'downloaded'" class="t-body-s ink-2" role="status" data-test="install">{{ t('inbox.skill.install', { name: data.name }) }}</p>
        <Card v-if="data.diff" :title="t('inbox.skill.diff')" data-test="diff"><DiffView :diff="data.diff" /></Card>
        <Card :title="t('inbox.skill.preview')"><CodeBlock :code="data.content" data-test="content" /></Card>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-3);
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
.facts {
  color: var(--ink-3);
}
</style>
