<script setup lang="ts">
import { Eye } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { Badge, Card, ListCard, ListRow, type Tone } from '../ui'

/** The print babysitter: whether it watches, its latest checks with their snapshots, and when it paused a print. */
const props = defineProps<{ babysitter: Schemas['BabysitterStatus'] }>()
const { t } = useI18n()
const format = useFormat()
const checks = computed(() => [...props.babysitter.verdicts].reverse())
const tone = (outcome: string): Tone => (outcome === 'ok' ? 'success' : outcome === 'failure' ? 'danger' : 'neutral')
</script>

<template>
  <Card :title="t('printers.babysitter.title')" data-test="babysitter">
    <p class="state">
      <Badge :tone="babysitter.active ? 'info' : 'neutral'" dot :label="babysitter.active ? t('printers.babysitter.watching') : t('printers.babysitter.idle')" />
    </p>
    <p v-if="babysitter.pausedAt" class="paused">{{ t('printers.babysitter.pausedAt', { time: format.dateTime(babysitter.pausedAt) }) }}</p>
    <ListCard v-if="checks.length">
      <ListRow v-for="check in checks" :key="check.at" :title="check.reason" :chevron="false">
        <template #lead><Eye :size="18" class="ink-3" aria-hidden="true" /></template>
        <template #subtitle>
          <span class="num">{{ format.time(check.at) }}</span>
          <template v-if="check.snapshotUrl"> · <a :href="check.snapshotUrl" target="_blank" rel="noopener">{{ t('printers.babysitter.snapshot') }}</a></template>
        </template>
        <template #trail><Badge :tone="tone(check.outcome)" :label="check.outcome" /></template>
      </ListRow>
    </ListCard>
    <p class="note">{{ t('printers.babysitter.note') }}</p>
  </Card>
</template>

<style scoped>
.state,
.paused {
  margin-bottom: var(--space-3);
}
.paused {
  color: var(--warning);
  font: 550 14px/20px var(--font-sans);
}
.note {
  margin-top: var(--space-3);
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
</style>
