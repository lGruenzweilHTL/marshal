<script setup lang="ts">
import { Ban, Check, CircleDot, Clock, X } from 'lucide-vue-next'
import type { Component } from 'vue'
import { useI18n } from 'vue-i18n'
import { useFormat } from '../i18n/locale'
import { AppIcon, Spinner } from '../ui'
import type { ToolState, ToolStep } from './turnEvents'

/**
 * The steps of a reply: one line per tool call (label, argument summary, state, duration, result summary), updated in
 * place when the call ends, and the turn's status messages. Summaries are plain text, never markdown.
 */
defineProps<{ steps: ToolStep[] }>()
const { t, te } = useI18n()
const format = useFormat()

const icons: Record<Exclude<ToolState, 'running'>, Component> = { ok: Check, error: X, denied: Ban, pending: Clock }

/** The tool's interface label (`chat.tools.<id>`), or the tool id when there is none. */
function label(tool: string) {
  const key = `chat.tools.${tool}`
  return te(key) ? t(key) : tool
}
</script>

<template>
  <ol class="timeline">
    <template v-for="(step, index) in steps" :key="step.kind === 'tool' ? step.id : `status-${index}`">
      <li v-if="step.kind === 'status'" class="step status" data-test="status-step">
        <AppIcon :icon="CircleDot" :size="14" class="icon" />
        <span class="text">{{ step.text }}</span>
      </li>
      <li v-else class="step tool" :data-state="step.state" data-test="tool-step">
        <Spinner v-if="step.state === 'running'" :size="14" class="icon" />
        <AppIcon v-else :icon="icons[step.state]" :size="14" class="icon" />
        <span class="body">
          <span class="head">
            <span class="label" data-test="tool-label">{{ label(step.tool) }}</span>
            <span v-if="step.arguments" class="arguments">{{ step.arguments }}</span>
          </span>
          <span class="meta">
            <span class="state">{{ t(`chat.toolState.${step.state}`) }}</span>
            <span v-if="step.state !== 'running' && step.durationMs">{{ format.duration(step.durationMs) }}</span>
            <span v-if="step.result" class="result">{{ step.result }}</span>
          </span>
        </span>
      </li>
    </template>
  </ol>
</template>

<style scoped>
.timeline {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.step {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  padding: 4px 0;
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
.icon {
  flex-shrink: 0;
  margin-top: 2px;
  color: var(--ink-3);
}
.tool[data-state='ok'] .icon {
  color: var(--success);
}
.tool[data-state='error'] .icon,
.tool[data-state='denied'] .icon {
  color: var(--danger);
}
.tool[data-state='pending'] .icon {
  color: var(--warning);
}
.body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.head {
  display: flex;
  flex-wrap: wrap;
  column-gap: var(--space-2);
}
.label {
  color: var(--ink);
  font-weight: 550;
}
.arguments {
  min-width: 0;
  color: var(--ink-3);
  font: var(--text-mono-s);
  line-height: 18px;
  overflow-wrap: anywhere;
}
.meta {
  display: flex;
  flex-wrap: wrap;
  column-gap: var(--space-2);
  color: var(--ink-3);
  font-size: 12px;
  line-height: 16px;
}
.result {
  min-width: 0;
  overflow-wrap: anywhere;
}
.status .text {
  overflow-wrap: anywhere;
}
</style>
