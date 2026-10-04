<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useApiSession } from '../api/inject'

const emit = defineEmits<{ changed: [active: boolean] }>()

const session = useApiSession()
const active = ref<boolean | null>(null)
const cancelled = ref<number | null>(null)
const confirming = ref(false)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data } = await session.raw.GET('/api/v1/tasks/kill-switch')
  if (data) active.value = data.active
  else error.value = 'The kill switch could not be loaded.'
})

async function activate() {
  confirming.value = false
  const { data } = await session.raw.POST('/api/v1/tasks/kill-switch/activate')
  if (!data) {
    error.value = 'Background tasks could not be stopped.'
    return
  }
  error.value = null
  active.value = true
  cancelled.value = data.cancelled ?? 0
  emit('changed', true)
}

async function release() {
  const { data } = await session.raw.POST('/api/v1/tasks/kill-switch/release')
  if (!data) {
    error.value = 'Background tasks could not be resumed.'
    return
  }
  error.value = null
  active.value = false
  cancelled.value = null
  emit('changed', false)
}
</script>

<template>
  <div data-test="kill-switch" class="kill-switch">
    <template v-if="active === true">
      <p>
        <span class="badge unhealthy">stopped</span> Background tasks are stopped until you resume them.
        <template v-if="cancelled !== null">{{ cancelled }} tasks cancelled.</template>
      </p>
      <button type="button" data-test="kill-switch-release" @click="release">Resume background tasks</button>
    </template>
    <template v-else-if="active === false">
      <p><span class="badge healthy">running normally</span> Background tasks run as scheduled.</p>
      <button v-if="!confirming" type="button" @click="confirming = true">Stop all background tasks</button>
      <span v-else class="confirm">
        Cancels every unfinished task.
        <button type="button" data-test="kill-switch-confirm" class="danger" @click="activate">Stop all now</button>
        <button type="button" @click="confirming = false">Keep running</button>
      </span>
    </template>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
  </div>
</template>

<style scoped>
.kill-switch p {
  margin: 0 0 0.5rem;
}

.confirm {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}

.danger {
  color: var(--error);
}
</style>
