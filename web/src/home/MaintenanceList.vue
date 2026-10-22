<script setup lang="ts">
import { Check } from 'lucide-vue-next'
import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, Dialog, ErrorBanner, ListCard, ProgressBar, useToasts, type Tone } from '../ui'

type Item = Schemas['MaintenanceItemStatus']

/**
 * Usage-based maintenance items in the server's order (due, then soon, then by name): usage against the interval and
 * a "done" action behind a confirmation, since it restarts the count.
 */
defineProps<{ items: Item[] }>()
const emit = defineEmits<{ changed: [] }>()
const api = useApi()
const format = useFormat()
const toasts = useToasts()
const { t } = useI18n()

const confirming = shallowRef<Item | null>(null)
const tones: Record<string, Tone> = { due: 'danger', soon: 'warning', ok: 'success' }
const bars: Record<string, 'danger' | 'warning' | 'accent'> = { due: 'danger', soon: 'warning' }

function used(item: Item) {
  const unit = item.unit ? ` ${item.unit}` : ''
  return `${format.value.number(item.usage ?? 0)} / ${format.value.number(item.every)}${unit}`
}

const done = useSubmit(async () => {
  const item = confirming.value!
  const result = await api.call((client) => client.POST('/api/v1/maintenance/{id}/done', { params: { path: { id: item.id } } }))
  if (!result.error) {
    confirming.value = null
    toasts.show(t('home.maintenance.doneToast', { name: item.name }))
    emit('changed')
  }
  return result
})
</script>

<template>
  <ListCard>
    <div v-for="item in items" :key="item.id" class="item" :data-maintenance="item.id">
      <div class="line">
        <span class="t-body name">{{ item.name }}</span>
        <Badge :tone="tones[item.state] ?? 'neutral'" :label="t(`home.maintenance.state.${item.state}`)" />
      </div>
      <template v-if="item.usage !== null">
        <ProgressBar :value="(item.usage / item.every) * 100" :tone="bars[item.state] ?? 'accent'" :label="t('home.maintenance.usage', { name: item.name })" />
        <div class="line t-body-s ink-2">
          <span class="num">{{ used(item) }}</span>
          <AppButton size="sm" variant="ghost" :icon="Check" data-test="done" @click="confirming = item">{{ t('home.maintenance.done') }}</AppButton>
        </div>
      </template>
      <p v-if="item.error" class="t-body-s ink-3">{{ item.error }}</p>
    </div>
  </ListCard>

  <Dialog
    :open="!!confirming"
    :title="t('home.maintenance.confirmTitle', { name: confirming?.name ?? '' })"
    :description="t('home.maintenance.confirmBody')"
    @close="confirming = null"
  >
    <ErrorBanner v-if="done.error.value" :error="done.error.value" />
    <template #actions>
      <AppButton @click="confirming = null">{{ t('actions.cancel') }}</AppButton>
      <AppButton variant="primary" data-test="confirm-done" :loading="done.busy.value" @click="done.submit()">{{ t('home.maintenance.done') }}</AppButton>
    </template>
  </Dialog>
</template>

<style scoped>
.item {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
}
.item + .item {
  border-top: 1px solid var(--line);
}
.line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  min-width: 0;
}
.name {
  min-width: 0;
  overflow-wrap: anywhere;
}
</style>
