<script setup lang="ts">
import { Brain, ChevronDown, ChevronRight } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { AppIcon } from '../ui'

/**
 * The model's reasoning above a reply: open while it streams, collapsed once the reply text starts, toggled by the
 * user. The text is shown as written (plain text, never markdown or HTML), so links and images in it stay inert.
 */
defineProps<{
  text: string
  /** Reasoning is still arriving. */
  streaming?: boolean
  /** Some reasoning followed a redacted tool result and was not kept. */
  withheld?: boolean
}>()
const open = defineModel<boolean>('open', { default: false })
const { t } = useI18n()

function onToggle(event: Event) {
  const next = (event.target as HTMLDetailsElement).open
  if (next !== open.value) open.value = next
}
</script>

<template>
  <details class="reasoning" :open="open" data-test="reasoning" @toggle="onToggle">
    <summary>
      <AppIcon :icon="open ? ChevronDown : ChevronRight" :size="14" />
      <AppIcon :icon="Brain" :size="14" />
      <span>{{ streaming ? t('chat.reasoning.streaming') : t('chat.reasoning.title') }}</span>
    </summary>
    <p v-if="text" class="text" data-test="reasoning-text">{{ text }}</p>
    <p v-if="withheld" class="withheld" data-test="reasoning-withheld">{{ t('chat.reasoning.withheld') }}</p>
  </details>
</template>

<style scoped>
.reasoning {
  align-self: stretch;
  color: var(--ink-3);
}
summary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  cursor: pointer;
  list-style: none;
  font: 500 13px/18px var(--font-sans);
}
summary::-webkit-details-marker {
  display: none;
}
summary:focus-visible {
  outline: var(--focus-ring);
  border-radius: var(--radius-xs);
}
.text,
.withheld {
  margin: var(--space-1) 0 0 6px;
  padding-left: var(--space-3);
  border-left: 2px solid var(--line-strong);
  font: 400 13px/20px var(--font-sans);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.withheld {
  font-style: italic;
}
</style>
