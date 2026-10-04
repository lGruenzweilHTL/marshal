<script setup lang="ts">
import { Camera, VideoOff } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, AppIcon, Card, ErrorBanner } from '../ui'

/** The webcam stream through Marshal (never straight from the printer) and an on-demand snapshot. */
const props = defineProps<{ printer: string }>()
const api = useApi()
const { t } = useI18n()
const stream = computed(() => `${api.baseUrl}/api/v1/printers/${encodeURIComponent(props.printer)}/webcam/stream`)
const snapshotUrl = shallowRef<string | null>(null)
const broken = shallowRef(false)

const snapshot = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/printers/{printer}/snapshots', { params: { path: { printer: props.printer } } }))
  if (result.data) snapshotUrl.value = result.data.url
  return result
})
</script>

<template>
  <Card flush>
    <div v-if="broken" class="frame off" role="img" :aria-label="t('printers.webcamOff')">
      <AppIcon :icon="VideoOff" :size="28" />
      <span>{{ t('printers.webcamOff') }}</span>
    </div>
    <img v-else data-test="webcam" class="frame" :src="stream" :alt="t('printers.webcamAlt', { printer })" @error="broken = true" />
    <div class="foot">
      <span class="t-label">{{ t('printers.webcam') }}</span>
      <AppButton size="sm" :icon="Camera" :loading="snapshot.busy.value" @click="snapshot.submit()">{{ t('printers.snapshot') }}</AppButton>
    </div>
    <ErrorBanner v-if="snapshot.error.value" :error="snapshot.error.value" />
    <img v-if="snapshotUrl" class="frame" :src="snapshotUrl" :alt="t('printers.snapshotAlt', { printer })" />
  </Card>
</template>

<style scoped>
.frame {
  display: block;
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  background: var(--surface-3);
}
.off {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  color: var(--ink-3);
  font: 400 14px/20px var(--font-sans);
}
.foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--space-3) var(--space-4);
}
</style>
