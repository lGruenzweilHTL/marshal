<script setup lang="ts">
import { Bookmark, Camera, VideoOff } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, AppIcon, Card, ErrorBanner, useToasts } from '../ui'

/**
 * The webcam stream through Marshal (never straight from the printer) and an on-demand snapshot, which "Keep" copies
 * into the artifact store (outside the snapshot retention).
 */
const props = defineProps<{ printer: string }>()
const api = useApi()
const { t } = useI18n()
const stream = computed(() => `${api.baseUrl}/api/v1/printers/${encodeURIComponent(props.printer)}/webcam/stream`)
const snapshotUrl = shallowRef<string | null>(null)
const snapshotId = shallowRef<string | null>(null)
const keptId = shallowRef<string | null>(null)
const toasts = useToasts()
const broken = shallowRef(false)

const snapshot = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/printers/{printer}/snapshots', { params: { path: { printer: props.printer } } }))
  if (result.data) {
    snapshotUrl.value = result.data.url
    snapshotId.value = result.data.id
    keptId.value = null
  }
  return result
})

const keep = useSubmit(async () => {
  const result = await api.call((client) =>
    client.POST('/api/v1/printers/{printer}/snapshots/{snapshot}/keep', { params: { path: { printer: props.printer, snapshot: snapshotId.value! } } }),
  )
  if (result.data) {
    keptId.value = result.data.id
    toasts.show(t('printers.kept'))
  }
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
    <template v-if="snapshotUrl">
      <img class="frame" :src="snapshotUrl" :alt="t('printers.snapshotAlt', { printer })" />
      <div class="foot">
        <AppButton v-if="keptId" size="sm" variant="ghost" :to="{ name: 'artifact', params: { id: keptId } }" data-test="open-kept">{{ t('printers.openKept') }}</AppButton>
        <span v-else />
        <AppButton size="sm" :icon="Bookmark" data-test="keep" :disabled="!!keptId" :loading="keep.busy.value" @click="keep.submit()">{{ t('printers.keep') }}</AppButton>
      </div>
      <ErrorBanner v-if="keep.error.value" :error="keep.error.value" />
    </template>
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
