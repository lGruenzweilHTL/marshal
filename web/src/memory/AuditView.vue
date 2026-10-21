<script setup lang="ts">
import { AlertTriangle, Check, CheckCircle2, Copy, History, Link2Off, Unlink, X } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { useApi } from '../api/inject'
import type { Schemas } from '../api/client'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, ErrorBanner, IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop } from '../ui'
import ChunkType from './ChunkType.vue'
import MemoryTabs from './MemoryTabs.vue'

type Contradiction = { current: string; outdated: string; closeAt: string; reason: string }
type Duplicate = { keep: string; other: string; title: string; content: string; similarity: number }

/**
 * What needs a look in memory: the idle-time jobs' proposals (accept or dismiss), contradictions, stale chunks,
 * orphans of deleted projects and broken links.
 */
const api = useApi()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const report = useLoad(() => api.call((client) => client.GET('/api/v1/memory/audit')), {
  isEmpty: (data) => !data.proposals?.length && !data.stale.length && !data.contradictions.length && !data.orphans.length && !data.danglingEdges.length,
})
const data = computed(() => report.data.value)

const contradiction = (proposal: Schemas['MemoryProposalView']) => proposal.suggestion as unknown as Contradiction
const duplicate = (proposal: Schemas['MemoryProposalView']) => proposal.suggestion as unknown as Duplicate

const decide = useSubmit(async (id: string, action: 'accept' | 'dismiss') => {
  const options = { params: { path: { id } } }
  const result = await api.call((client) =>
    action === 'accept' ? client.POST('/api/v1/memory/proposals/{id}/accept', options) : client.POST('/api/v1/memory/proposals/{id}/dismiss', options),
  )
  if (!result.error) report.refresh()
  return result
})
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('memory.title')" :subtitle="desktop ? t('memory.audit.intro') : undefined" />
    <MemoryTabs />
    <LoadBoundary :load="report" size="page" :empty-title="t('memory.audit.clean.title')" :empty-body="t('memory.audit.clean.body')" :empty-icon="CheckCircle2">
      <template v-if="data">
        <Section v-if="data.proposals?.length" :title="t('memory.audit.proposals')" :count="data.proposals.length" data-section="proposals">
          <p class="hint">{{ t('memory.audit.proposalsHint') }}</p>
          <ErrorBanner v-if="decide.error.value" :error="decide.error.value" />
          <ul class="pairs">
            <li v-for="proposal in data.proposals" :key="proposal.id" class="pair proposal" :data-proposal="proposal.id">
              <IconTile :icon="proposal.kind === 'duplicate' ? Copy : AlertTriangle" :tone="proposal.kind === 'duplicate' ? 'info' : 'warning'" />
              <div class="body">
                <Badge :tone="proposal.kind === 'duplicate' ? 'info' : 'warning'" :label="t(`memory.audit.proposalKind.${proposal.kind}`)" />
                <div class="sides" :class="{ split: desktop }">
                  <div
                    v-for="side in [proposal.a, proposal.b]"
                    :key="side.id"
                    class="side"
                    :data-test="proposal.kind === 'contradiction' ? (side.id === contradiction(proposal).outdated ? 'outdated' : 'current') : undefined"
                  >
                    <span class="line">
                      <ChunkType :type="side.type" />
                      <span v-if="proposal.kind === 'contradiction'" class="verdict" :class="{ outdated: side.id === contradiction(proposal).outdated }">{{
                        side.id === contradiction(proposal).outdated
                          ? t('memory.audit.outdated', { date: format.date(contradiction(proposal).closeAt) })
                          : t('memory.audit.current')
                      }}</span>
                    </span>
                    <RouterLink :to="`/memory/chunks/${side.id}`" class="title">{{ side.title }}</RouterLink>
                    <p class="content">{{ side.content }}</p>
                  </div>
                </div>
                <p v-if="proposal.kind === 'contradiction' && contradiction(proposal).reason" class="reason">{{ contradiction(proposal).reason }}</p>
                <div v-if="proposal.kind === 'duplicate'" class="merged">
                  <span class="t-overline">{{ t('memory.audit.merged') }} · <span class="num">{{ t('memory.audit.similarity', { percent: Math.round(duplicate(proposal).similarity * 100) }) }}</span></span>
                  <strong>{{ duplicate(proposal).title }}</strong>
                  <p>{{ duplicate(proposal).content }}</p>
                </div>
                <div class="decide">
                  <AppButton size="sm" variant="primary" :icon="Check" data-test="accept" :disabled="decide.busy.value" @click="decide.submit(proposal.id, 'accept')">{{ t('memory.audit.accept') }}</AppButton>
                  <AppButton size="sm" variant="ghost" :icon="X" data-test="dismiss" :disabled="decide.busy.value" @click="decide.submit(proposal.id, 'dismiss')">{{ t('memory.audit.dismiss') }}</AppButton>
                </div>
              </div>
            </li>
          </ul>
        </Section>

        <Section v-if="data.contradictions.length" :title="t('memory.audit.contradictions')" :count="data.contradictions.length" data-section="contradictions">
          <p class="hint">{{ t('memory.audit.contradictionsHint') }}</p>
          <ul class="pairs">
            <li v-for="pair in data.contradictions" :key="`${pair.from.id}/${pair.to.id}`" class="pair">
              <IconTile :icon="AlertTriangle" tone="warning" />
              <div class="sides" :class="{ split: desktop }">
                <div v-for="(side, index) in [pair.from, pair.to]" :key="side.id" class="side">
                  <span class="line"><ChunkType :type="side.type" /><span class="num ink-3">{{ format.date(side.updatedAt) }}</span></span>
                  <RouterLink :to="`/memory/chunks/${side.id}`" class="title">{{ side.title }}</RouterLink>
                  <p class="content">{{ side.content }}</p>
                  <span v-if="index === 0" class="sr-only">{{ t('memory.audit.contradicts') }}</span>
                </div>
              </div>
            </li>
          </ul>
        </Section>

        <Section v-if="data.stale.length" :title="t('memory.audit.stale')" :count="data.stale.length" data-section="stale">
          <p class="hint">{{ t('memory.audit.staleHint') }}</p>
          <ListCard>
            <ListRow v-for="chunk in data.stale" :key="chunk.id" :title="chunk.title" :subtitle="t('memory.audit.updated', { time: format.dateTime(chunk.updatedAt) })" :to="`/memory/chunks/${chunk.id}`">
              <template #lead><IconTile :icon="History" tone="warning" /></template>
            </ListRow>
          </ListCard>
        </Section>

        <Section v-if="data.orphans.length" :title="t('memory.audit.orphans')" :count="data.orphans.length" data-section="orphans">
          <p class="hint">{{ t('memory.audit.orphansHint') }}</p>
          <ListCard>
            <ListRow v-for="chunk in data.orphans" :key="chunk.id" :title="chunk.title" :subtitle="chunk.content" :to="`/memory/chunks/${chunk.id}`">
              <template #lead><IconTile :icon="Unlink" /></template>
            </ListRow>
          </ListCard>
        </Section>

        <Section v-if="data.danglingEdges.length" :title="t('memory.audit.dangling')" :count="data.danglingEdges.length" data-section="dangling">
          <ListCard>
            <ListRow v-for="edge in data.danglingEdges" :key="`${edge.from}/${edge.kind}/${edge.to}`" :to="`/memory/chunks/${edge.from}`" :subtitle="`${edge.kind} → ${edge.to}`">
              <template #lead><IconTile :icon="Link2Off" /></template>
              <template #title><span class="t-mono-s">{{ edge.from }}</span></template>
            </ListRow>
          </ListCard>
        </Section>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.hint {
  margin-bottom: var(--space-3);
  color: var(--ink-3);
  font: var(--text-body-s);
}
.pairs {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin: 0;
  padding: 0;
  list-style: none;
}
.pair {
  display: flex;
  gap: var(--space-3);
  padding: var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.body {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  align-items: flex-start;
  gap: var(--space-3);
  min-width: 0;
}
.body > .sides {
  align-self: stretch;
}
.verdict {
  color: var(--success);
  font-weight: 600;
}
.verdict.outdated {
  color: var(--warning);
}
.reason,
.merged {
  color: var(--ink-2);
  font: var(--text-body-s);
}
.merged {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-self: stretch;
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--surface-2);
}
.merged strong {
  color: var(--ink);
}
.decide {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.sides {
  display: grid;
  flex-grow: 1;
  gap: var(--space-3);
  min-width: 0;
}
.sides.split {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.side {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: var(--space-4);
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  overflow-wrap: anywhere;
}
.line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  font: var(--text-body-s);
}
.title {
  color: var(--ink);
  font-weight: 600;
}
.content {
  color: var(--ink-2);
}
</style>
