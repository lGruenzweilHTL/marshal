<script setup lang="ts">
import { AlertTriangle, CheckCircle2, History, Link2Off, Unlink } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop } from '../ui'
import ChunkType from './ChunkType.vue'
import MemoryTabs from './MemoryTabs.vue'

/** What needs a look in memory: contradictions, stale chunks, orphans of deleted projects and broken links. */
const api = useApi()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const report = useLoad(() => api.call((client) => client.GET('/api/v1/memory/audit')), {
  isEmpty: (data) => !data.stale.length && !data.contradictions.length && !data.orphans.length && !data.danglingEdges.length,
})
const data = computed(() => report.data.value)
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('memory.title')" :subtitle="desktop ? t('memory.audit.intro') : undefined" />
    <MemoryTabs />
    <LoadBoundary :load="report" size="page" :empty-title="t('memory.audit.clean.title')" :empty-body="t('memory.audit.clean.body')" :empty-icon="CheckCircle2">
      <template v-if="data">
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
