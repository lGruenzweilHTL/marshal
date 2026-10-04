<script setup lang="ts">
import { AlertTriangle, ChevronRight } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { loadHealth } from '../system/health'
import { AppIcon } from '../ui'

/** A strip at the top of Home when a health check reports a problem; silent while everything is fine. */
const api = useApi()
const { t } = useI18n()
const health = useLoad(() => loadHealth(api), { isEmpty: (report) => report.status === 'healthy' })

const title = computed(() =>
  health.state.value === 'error' ? t('home.healthFailed') : health.data.value?.status === 'unhealthy' ? t('home.healthDown') : t('home.healthProblem'),
)
const details = computed(() =>
  Object.entries(health.data.value?.checks ?? {})
    .filter(([, check]) => check.status !== 'healthy')
    .map(([name, check]) => [name, ...(check.warnings ?? []), check.description].filter(Boolean).join(': '))
    .join(' · '),
)
</script>

<template>
  <RouterLink v-if="health.state.value === 'ready' || health.state.value === 'error'" to="/system" class="strip" :class="health.data.value?.status" data-test="health">
    <AppIcon :icon="AlertTriangle" :size="20" class="lead" />
    <span class="text">
      <span class="title">{{ title }}</span>
      <span v-if="details" class="details">{{ details }}</span>
    </span>
    <AppIcon :icon="ChevronRight" :size="18" />
  </RouterLink>
</template>

<style scoped>
.strip {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-3) 14px;
  border-radius: var(--radius-lg);
  background: var(--warning-soft);
  color: var(--ink);
  text-decoration: none;
}
.strip.unhealthy {
  background: var(--danger-soft);
}
.lead {
  color: var(--warning);
}
.unhealthy .lead {
  color: var(--danger);
}
.text {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  min-width: 0;
}
.title {
  font: 650 14px/20px var(--font-sans);
}
.details {
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
  overflow-wrap: anywhere;
}
</style>
