<script setup lang="ts">
import { House, Mic, MessageSquare, Plug, Zap } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { satelliteName } from '../format'
import { useFormat } from '../i18n/locale'
import { Badge, IconTile, ListRow, type Tone } from '../ui'
import TaintFlag from './TaintFlag.vue'

/** One run in a list: where it came from, its trigger and time, the outcome and what it cost. */
withDefaults(defineProps<{ run: Schemas['RunSummary']; dated?: boolean }>(), { dated: false })
const { t } = useI18n()
const format = useFormat()
const icons = { voice: Mic, web: MessageSquare, task: Zap, api: Plug, automation: House }
const tones: Record<Schemas['RunOutcome'], Tone> = { ok: 'success', error: 'danger', backgrounded: 'warning', cancelled: 'neutral' }
</script>

<template>
  <ListRow :to="`/runs/${run.id}`" :chevron="false" :data-run="run.id">
    <template #lead><IconTile :icon="icons[run.source]" /></template>
    <template #title>{{ [t(`runs.source.${run.source}`), run.satelliteId && satelliteName(run.satelliteId)].filter(Boolean).join(' · ') }}</template>
    <template #subtitle>
      {{ run.trigger }} · <span class="num">{{ dated ? format.dateTime(run.startedAt) : format.time(run.startedAt) }}</span>
      <template v-if="run.inputMode === 'voice' && run.timeToFirstDeltaMs != null">
        · <span class="num" data-test="first-delta">{{ t('runs.firstDelta', { time: format.duration(run.timeToFirstDeltaMs) }) }}</span>
      </template>
      <template v-if="run.tainted"> · <TaintFlag /></template>
    </template>
    <template #trail>
      <span class="trail">
        <Badge :tone="tones[run.outcome]" :label="t(`runs.outcome.${run.outcome}`)" />
        <span class="num cost">{{ format.cost(run.cost) }}</span>
      </span>
    </template>
  </ListRow>
</template>

<style scoped>
.trail {
  display: flex;
  align-items: center;
  gap: 10px;
}
.cost {
  color: var(--ink-2);
  font: 450 13px/18px var(--font-mono);
}
</style>
