<script setup lang="ts">
import { Play, Power } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { AppButton, AppIcon, Dialog, ErrorBanner, LoadState } from '../ui'

/**
 * Stops all background work (running tasks are cancelled, no task starts) until the user resumes. Stopping asks
 * first; resuming does not. `running` is the number of running tasks the caller knows of.
 */
const props = withDefaults(defineProps<{ running?: number }>(), { running: 0 })
const emit = defineEmits<{ changed: [active: boolean] }>()
const api = useApi()
const { t } = useI18n()

const state = useLoad(() => api.call((client) => client.GET('/api/v1/tasks/kill-switch')), { isEmpty: () => false })
const decided = shallowRef<{ active: boolean; cancelled?: number | null } | null>(null)
const active = computed(() => (decided.value ?? state.data.value)?.active ?? false)
const cancelled = computed(() => decided.value?.cancelled ?? null)
const asking = shallowRef(false)

const toggle = useSubmit(async (stop: boolean) => {
  const result = await api.call((client) => client.POST(stop ? '/api/v1/tasks/kill-switch/activate' : '/api/v1/tasks/kill-switch/release'))
  asking.value = false
  if (result.data) {
    decided.value = result.data
    emit('changed', result.data.active)
  }
  return result
})

const subtitle = computed(() =>
  active.value
    ? cancelled.value !== null
      ? t('tasks.killSwitch.cancelled', { count: cancelled.value })
      : t('tasks.killSwitch.stoppedBody')
    : t('tasks.killSwitch.running', props.running),
)
</script>

<template>
  <LoadState v-if="state.state.value === 'loading' || state.state.value === 'error'" :state="state.state.value" :error="state.error.value" @retry="state.retry()" />
  <section v-else class="kill" :class="{ stopped: active }" aria-live="polite" data-test="kill-switch">
    <span class="icon"><AppIcon :icon="Power" :size="20" /></span>
    <span class="text">
      <span class="title">{{ active ? t('tasks.killSwitch.stoppedTitle') : t('tasks.killSwitch.title') }}</span>
      <span class="sub num">{{ subtitle }}</span>
    </span>
    <AppButton v-if="active" :icon="Play" data-test="resume" :loading="toggle.busy.value" @click="toggle.submit(false)">{{ t('tasks.killSwitch.resume') }}</AppButton>
    <AppButton v-else variant="danger" :icon="Power" data-test="stop" @click="asking = true">{{ t('tasks.killSwitch.stop') }}</AppButton>
  </section>
  <ErrorBanner v-if="toggle.error.value && !asking" :error="toggle.error.value" />
  <Dialog :open="asking" :title="t('tasks.killSwitch.confirmTitle')" :description="t('tasks.killSwitch.confirmBody')" @close="asking = false">
    <ErrorBanner v-if="toggle.error.value" :error="toggle.error.value" />
    <template #actions>
      <AppButton @click="asking = false">{{ t('actions.cancel') }}</AppButton>
      <AppButton variant="danger" :icon="Power" data-test="confirm-stop" :loading="toggle.busy.value" @click="toggle.submit(true)">{{ t('tasks.killSwitch.confirm') }}</AppButton>
    </template>
  </Dialog>
</template>

<style scoped>
.kill {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 14px 14px 14px var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.kill.stopped {
  background: var(--danger-soft);
  box-shadow: none;
}
.icon {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  color: var(--ink-2);
}
.stopped .icon {
  background: var(--danger-fill);
  color: var(--danger-ink);
}
.text {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  gap: 2px;
  min-width: 0;
}
.title {
  font: 600 15px/20px var(--font-sans);
}
.stopped .title {
  color: var(--danger);
}
.sub {
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
</style>
