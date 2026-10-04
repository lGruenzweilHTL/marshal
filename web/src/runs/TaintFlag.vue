<script setup lang="ts">
import { ShieldAlert } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { AppIcon } from '../ui'

/** Marks a run that read untrusted content (mail, RSS, notes, web pages). */
const { t } = useI18n()
</script>

<template>
  <span class="taint" :title="t('runs.taintedHint')"><AppIcon :icon="ShieldAlert" :size="14" />{{ t('runs.tainted') }}</span>
</template>

<style scoped>
.taint {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  color: var(--warning);
  font: 600 12px/16px var(--font-sans);
}
</style>
