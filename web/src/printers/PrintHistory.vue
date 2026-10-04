<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import type { Load } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { Badge, ListCard, ListRow, LoadBoundary, type Tone } from '../ui'

/** The printer's last jobs with their result. */
defineProps<{ load: Load<Schemas['PrintJob'][]> }>()
const { t } = useI18n()
const format = useFormat()
const tone = (status: string): Tone => (status === 'completed' ? 'success' : status === 'cancelled' ? 'neutral' : status === 'in_progress' ? 'info' : 'danger')
</script>

<template>
  <LoadBoundary :load="load" :empty-title="t('printers.noHistory')">
    <template #default="{ data }">
      <ListCard>
        <ListRow v-for="job in data" :key="job.jobId" :chevron="false">
          <template #title><span class="t-mono-s">{{ job.filename }}</span></template>
          <template #subtitle><span class="num">{{ format.dateTime(job.startedAt) }} · {{ format.duration(job.printDuration * 1000) }}</span></template>
          <template #trail><Badge :tone="tone(job.status)" :label="job.status" /></template>
        </ListRow>
      </ListCard>
    </template>
  </LoadBoundary>
</template>
