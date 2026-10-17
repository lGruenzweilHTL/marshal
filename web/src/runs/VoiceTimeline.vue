<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'

/**
 * Where a voice turn's time went: context build, model calls (with time to first token) and tool calls laid end to
 * end on one scale from the request to the end of the run, with the first spoken delta marked.
 */
const props = defineProps<{ steps: Schemas['RunStepDetail'][]; totalMs: number; firstDeltaMs: number | null }>()
const { t } = useI18n()
const format = useFormat()

interface Segment {
  sequence: number
  kind: Schemas['RunStepKind']
  label: string
  start: number
  duration: number
  firstToken: number | null
}

const segments = computed<Segment[]>(() => {
  let start = 0
  const timed = props.steps.filter((step) => step.depth === 0 && step.kind !== 'reply')
  return timed.map((step) => {
    const segment: Segment = {
      sequence: step.sequence,
      kind: step.kind,
      label: step.tool ?? t(`runs.detail.kind.${step.kind}`),
      start,
      duration: step.durationMs,
      firstToken: step.kind === 'modelCall' ? step.firstTokenMs : null,
    }
    start += step.durationMs
    return segment
  })
})
const scale = computed(() => Math.max(props.totalMs, ...segments.value.map((segment) => segment.start + segment.duration), props.firstDeltaMs ?? 0, 1))
const percent = (milliseconds: number) => `${Math.round((milliseconds / scale.value) * 1000) / 10}%`
</script>

<template>
  <div class="timeline" data-test="voice-timeline">
    <div class="head">
      <span class="t-overline">{{ t('runs.detail.timeline.title') }}</span>
      <span class="num ink-3">{{ format.duration(totalMs) }}</span>
    </div>
    <div class="track" role="img" :aria-label="t('runs.detail.timeline.label')">
      <span
        v-for="segment in segments"
        :key="segment.sequence"
        class="segment"
        :class="segment.kind"
        :data-segment="segment.kind"
        :style="{ left: percent(segment.start), width: percent(segment.duration) }"
        :title="`${segment.label}: ${format.duration(segment.duration)}`"
      >
        <span class="label">{{ segment.label }}</span>
        <span v-if="segment.firstToken !== null" class="token" data-test="first-token" :style="{ left: `${segment.duration > 0 ? Math.min((segment.firstToken / segment.duration) * 100, 100) : 0}%` }" />
      </span>
      <span v-if="firstDeltaMs !== null" class="delta" data-test="first-delta" :style="{ left: percent(firstDeltaMs) }">
        <span class="delta-label num">{{ t('runs.detail.timeline.firstDelta', { time: format.duration(firstDeltaMs) }) }}</span>
      </span>
    </div>
    <ul class="legend">
      <li><span class="swatch context" />{{ t('runs.detail.kind.context') }}</li>
      <li><span class="swatch modelCall" />{{ t('runs.detail.kind.modelCall') }}</li>
      <li><span class="swatch toolCall" />{{ t('runs.detail.kind.toolCall') }}</li>
    </ul>
  </div>
</template>

<style scoped>
.timeline {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.track {
  position: relative;
  height: 44px;
  margin-top: 22px;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.segment {
  position: absolute;
  top: 6px;
  bottom: 6px;
  min-width: 2px;
  overflow: visible;
  border-radius: 4px;
}
.segment.context {
  background: var(--ink-3);
}
.segment.modelCall {
  background: var(--info);
}
.segment.toolCall {
  background: var(--warning);
}
.label {
  position: absolute;
  inset: 0 4px;
  overflow: hidden;
  color: var(--surface);
  font: 600 11px/32px var(--font-sans);
  white-space: nowrap;
  text-overflow: ellipsis;
}
.token {
  position: absolute;
  top: -4px;
  bottom: -4px;
  width: 2px;
  background: var(--ink);
}
.delta {
  position: absolute;
  top: -22px;
  bottom: 0;
  width: 2px;
  background: var(--success);
}
.delta-label {
  position: absolute;
  top: 0;
  left: 6px;
  color: var(--success);
  font: 600 11px/16px var(--font-sans);
  white-space: nowrap;
}
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  margin: 0;
  padding: 0;
  color: var(--ink-3);
  font: var(--text-body-s);
  list-style: none;
}
.legend li {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.swatch {
  width: 10px;
  height: 10px;
  border-radius: 3px;
}
.swatch.context {
  background: var(--ink-3);
}
.swatch.modelCall {
  background: var(--info);
}
.swatch.toolCall {
  background: var(--warning);
}
</style>
