<script setup lang="ts">
import { Box } from 'lucide-vue-next'
import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../../api/inject'
import { useFormat } from '../../i18n/locale'
import { AppButton, Banner, Spinner } from '../../ui'
import { parseModel, stage, type ModelStage } from './modelScene'

/** STL and 3MF in a WebGL canvas with orbit controls; models over 50 MB load only after the user asks. */
const props = defineProps<{ id: string; name: string; size: number }>()
const largeBytes = 50 * 1024 * 1024
const api = useApi()
const format = useFormat()
const { t } = useI18n()
const canvas = ref<HTMLCanvasElement | null>(null)
const state = shallowRef<'ask' | 'loading' | 'ready' | 'failed'>(props.size > largeBytes ? 'ask' : 'loading')
let current: ModelStage | null = null

async function load() {
  state.value = 'loading'
  const result = await api.fetch(`/api/v1/artifacts/${encodeURIComponent(props.id)}/content`)
  if (result.error || !result.data.ok) {
    state.value = 'failed'
    return
  }
  try {
    const model = parseModel(props.name, await result.data.arrayBuffer())
    state.value = 'ready'
    current = stage(canvas.value!, model)
  } catch {
    state.value = 'failed'
  }
}

onMounted(() => {
  if (state.value === 'loading') void load()
})
onBeforeUnmount(() => current?.dispose())
</script>

<template>
  <div class="model" :data-state="state">
    <canvas ref="canvas" class="canvas" :class="{ hidden: state !== 'ready' }" data-test="model-canvas"></canvas>
    <p v-if="state === 'ready'" class="t-body-s ink-2" data-test="controls">{{ t('artifacts.model.controls') }}</p>
    <div v-else-if="state === 'ask'" class="ask">
      <p>{{ t('artifacts.model.large', { size: format.bytes(size) }) }}</p>
      <AppButton :icon="Box" data-test="load-model" @click="load()">{{ t('artifacts.model.load') }}</AppButton>
    </div>
    <Spinner v-else-if="state === 'loading'" />
    <Banner v-else tone="danger" :title="t('artifacts.model.failed')" />
  </div>
</template>

<style scoped>
.model {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.canvas {
  width: 100%;
  aspect-ratio: 4 / 3;
  border-radius: var(--radius-md);
  background: var(--surface-2);
  touch-action: none;
}
.canvas.hidden {
  display: none;
}
.ask {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-3);
}
</style>
