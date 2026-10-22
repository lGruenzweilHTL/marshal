<script setup lang="ts">
import { BellOff, Bell, RotateCcw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, ErrorBanner } from '../ui'

type Detector = Schemas['DetectorItem']

/**
 * Suggestion detectors with how often the user accepted them: mute one for a week or until unmuted, or reset its
 * counts so it starts exploring again. Changes apply at once; they are not part of the preferences form.
 */
defineProps<{ detectors: Detector[] }>()
const emit = defineEmits<{ changed: [Detector] }>()
const api = useApi()
const format = useFormat()
const { t, te } = useI18n()
const week = 7 * 24 * 60 * 60 * 1000

const label = (id: string) => (te(`preferences.detectors.names.${id}`) ? t(`preferences.detectors.names.${id}`) : id)

const change = useSubmit(async (detector: string, action: 'week' | 'mute' | 'unmute' | 'reset') => {
  const params = { params: { path: { detector } } }
  const result = await api.call((client) =>
    action === 'unmute'
      ? client.DELETE('/api/v1/suggestions/detectors/{detector}/mute', params)
      : action === 'reset'
        ? client.POST('/api/v1/suggestions/detectors/{detector}/reset', params)
        : client.PUT('/api/v1/suggestions/detectors/{detector}/mute', {
            ...params,
            body: { until: action === 'week' ? new Date(Date.now() + week).toISOString() : null },
          }),
  )
  if (result.data) emit('changed', result.data)
  return result
})

</script>

<template>
  <ul class="detectors">
    <li v-for="item in detectors" :key="item.detector" class="detector" :data-detector="item.detector">
      <div class="line">
        <span class="t-body name">{{ label(item.detector) }}</span>
        <Badge
          v-if="item.muted"
          tone="neutral"
          :label="item.mutedUntil ? t('preferences.detectors.mutedUntil', { time: format.dateTime(item.mutedUntil) }) : t('preferences.detectors.muted')"
        />
      </div>
      <span class="t-body-s ink-2">
        {{ t('preferences.detectors.counts', { accepted: item.accepted, answered: item.accepted + item.dismissed }) }} ·
        {{ t('preferences.detectors.estimate', { estimate: format.percent(item.estimate) }) }}
      </span>
      <div class="actions">
        <template v-if="item.muted">
          <AppButton size="sm" :icon="Bell" data-test="unmute" :disabled="change.busy.value" @click="change.submit(item.detector, 'unmute')">
            {{ t('preferences.detectors.unmute') }}
          </AppButton>
        </template>
        <template v-else>
          <AppButton size="sm" :icon="BellOff" data-test="mute-week" :disabled="change.busy.value" @click="change.submit(item.detector, 'week')">
            {{ t('preferences.detectors.muteWeek') }}
          </AppButton>
          <AppButton size="sm" variant="ghost" data-test="mute" :disabled="change.busy.value" @click="change.submit(item.detector, 'mute')">
            {{ t('preferences.detectors.mute') }}
          </AppButton>
        </template>
        <AppButton size="sm" variant="ghost" :icon="RotateCcw" data-test="reset" :disabled="change.busy.value" @click="change.submit(item.detector, 'reset')">
          {{ t('preferences.detectors.reset') }}
        </AppButton>
      </div>
    </li>
  </ul>
  <ErrorBanner v-if="change.error.value" :error="change.error.value" />
</template>

<style scoped>
.detectors {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0;
  list-style: none;
}
.detector {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding: var(--space-3) 0;
}
.detector + .detector {
  border-top: 1px solid var(--line);
}
.line,
.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.line {
  justify-content: space-between;
}
.actions {
  margin-top: var(--space-1);
}
</style>
