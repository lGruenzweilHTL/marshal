<script setup lang="ts">
import { Check, GitMerge, X } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { useInboxCountOptional } from '../layout/inboxCount'
import { AppButton, AppIcon, Badge, ErrorBanner, LoadBoundary, LoadState, Page, PageHeader, type Tone } from '../ui'
import DiffView from './DiffView.vue'
import { diffStats } from './diffStats'

/** A change proposal from a coding session: the diff, merge or reject. Nothing changes until the user merges. */
const api = useApi()
const inbox = useInboxCountOptional()
const route = useRoute()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

const proposal = useLoad(() => api.call((client) => client.GET('/api/v1/proposals/{id}', { params: { path: { id } } })))
// The decision's answer is the proposal as it is now; shown instead of the loaded one.
const decided = shallowRef<Schemas['ProposalRecord'] | null>(null)
const current = computed(() => decided.value ?? proposal.data.value)
const stats = computed(() => diffStats(proposal.data.value?.diff ?? ''))
const tones: Record<string, Tone> = { open: 'info', merged: 'success', rejected: 'neutral' }

const decision = useSubmit(async (merge: boolean) => {
  const result = await api.call((client) =>
    client.POST(merge ? '/api/v1/proposals/{id}/merge' : '/api/v1/proposals/{id}/reject', { params: { path: { id } } }),
  )
  if (!result.error) {
    decided.value = result.data
    void inbox?.refresh()
  }
  return result
})
</script>

<template>
  <Page width="wide">
    <LoadBoundary :load="proposal" size="page">
      <template #default>
        <template v-if="current">
        <PageHeader :title="current.title" :subtitle="t('inbox.proposal.intro')" back="/inbox" :back-label="t('inbox.title')">
          <template v-if="current.status === 'open'" #actions>
            <AppButton :icon="X" data-test="reject" :disabled="decision.busy.value" @click="decision.submit(false)">{{ t('actions.reject') }}</AppButton>
            <AppButton variant="primary" :icon="GitMerge" data-test="merge" :loading="decision.busy.value" @click="decision.submit(true)">
              {{ t('inbox.proposal.merge') }}
            </AppButton>
          </template>
        </PageHeader>
        <div class="meta">
          <Badge :tone="tones[current.status]" :label="t(`inbox.proposal.status.${current.status}`)" />
          <span class="t-mono">{{ current.target }} · {{ current.branch }}</span>
          <span class="facts">
            <RouterLink v-if="current.taskId" :to="`/tasks/${current.taskId}`">{{ t('inbox.proposal.fromTask') }}</RouterLink>
            · <span class="num">{{ format.dateTime(current.createdAt) }}</span> · {{ t('inbox.proposal.files', stats.files) }},
            <span class="num" data-test="stat"><span class="plus">+{{ stats.added }}</span> <span class="minus">−{{ stats.removed }}</span></span>
          </span>
        </div>
        <ErrorBanner v-if="decision.error.value" :error="decision.error.value" />
        <p v-if="current.status !== 'open'" class="result" role="status">
          <AppIcon :icon="current.status === 'merged' ? Check : X" :size="18" />
          {{ current.status === 'merged' ? t('inbox.proposal.merged') : t('inbox.proposal.rejected') }}
        </p>
        <DiffView v-if="current.diff" :diff="current.diff" />
        <LoadState v-else state="empty" :title="t('inbox.proposal.noDiff')" />
        </template>
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
.plus {
  color: var(--success);
}
.minus {
  color: var(--danger);
}
.result {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--target);
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  background: var(--surface-3);
  color: var(--ink-2);
  font: 600 14px/20px var(--font-sans);
}
</style>
