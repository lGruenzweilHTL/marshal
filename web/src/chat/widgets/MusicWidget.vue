<script setup lang="ts">
import { Music, Pause, Play, SkipBack, SkipForward } from 'lucide-vue-next'
import { onBeforeUnmount, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../../api/client'
import { useApi } from '../../api/inject'
import type { ApiError } from '../../api/errors'
import { AppButton, ErrorBanner, IconTile } from '../../ui'

export interface MusicItem {
  name: string
  artists: string[]
  album: string | null
}

/** The `music` widget's data, as the turn and `GET /api/v1/music/{speaker}` return it. */
export interface MusicWidgetData {
  speaker: string
  state: string
  current: MusicItem | null
  next: MusicItem | null
  items: number
  shuffle: boolean
  repeat: string | null
}

/**
 * The `music` widget: what plays on a speaker and what comes next, with play / pause, previous, next and volume.
 * It refreshes every 5 seconds while shown; names come from music providers and render as text only.
 */
const props = defineProps<{ data: MusicWidgetData }>()
const { t } = useI18n()
const api = useApi()
const view = shallowRef<MusicWidgetData>(props.data)
const volume = shallowRef(50)
const approval = shallowRef(false)

async function refresh() {
  const result = await api.call((client) => client.GET('/api/v1/music/{speaker}', { params: { path: { speaker: props.data.speaker } } }))
  if (result.data) view.value = result.data as unknown as MusicWidgetData
}

const error = shallowRef<ApiError | null>(null)

/** Sends one control; controls do not wait for each other, so quick clicks all reach the speaker. */
async function send(body: Schemas['MusicControlRequest']) {
  const result = await api.call((client) => client.POST('/api/v1/music/{speaker}/control', { params: { path: { speaker: props.data.speaker } }, body }))
  error.value = result.error ?? null
  approval.value = result.data?.status === 'approval_required'
  if (result.data?.status === 'done') void refresh()
}

let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  void refresh()
  timer = setInterval(() => void refresh(), 5000)
})
onBeforeUnmount(() => clearInterval(timer))

let sentVolume: number | null = null
/** Sends the slider's volume once per value; browsers may fire `change` more than once for one drag. */
function setVolume() {
  if (volume.value === sentVolume) return
  sentVolume = volume.value
  void send({ action: 'volume', volume: volume.value })
}

const artists = (item: MusicItem) => item.artists.join(', ')
</script>

<template>
  <article data-widget="music" class="music" :aria-label="t('chat.widgets.music.label', { speaker: view.speaker })">
    <div class="head">
      <IconTile :icon="Music" :size="40" />
      <span v-if="view.current" class="names">
        <span class="name">{{ view.current.name }}</span>
        <span class="by">{{ artists(view.current) }}<template v-if="view.current.album"> · {{ view.current.album }}</template></span>
      </span>
      <span v-else class="names by">{{ t('chat.widgets.music.nothing') }}</span>
    </div>
    <p v-if="view.next" class="next">
      <span class="label">{{ t('chat.widgets.music.next') }}</span>
      <span>{{ view.next.name }} · {{ artists(view.next) }}</span>
    </p>
    <p v-if="view.items > 0" class="count">{{ t('chat.widgets.music.items', { count: view.items }) }}</p>
    <div class="controls">
      <AppButton icon-only :icon="SkipBack" :label="t('chat.widgets.music.previousTrack')" data-control="previous" @click="send({ action: 'previous' })" />
      <AppButton
        v-if="view.state === 'playing'"
        icon-only
        variant="primary"
        :icon="Pause"
        :label="t('chat.widgets.music.pause')"
        data-control="pause"
        @click="send({ action: 'pause' })"
      />
      <AppButton v-else icon-only variant="primary" :icon="Play" :label="t('chat.widgets.music.play')" data-control="resume" @click="send({ action: 'resume' })" />
      <AppButton icon-only :icon="SkipForward" :label="t('chat.widgets.music.nextTrack')" data-control="next" @click="send({ action: 'next' })" />
      <label class="volume">
        <span class="sr-only">{{ t('chat.widgets.music.volume') }}</span>
        <input v-model.number="volume" type="range" name="volume" min="0" max="100" step="5" @change="setVolume" />
      </label>
    </div>
    <p v-if="approval" class="approval" role="status">{{ t('chat.widgets.music.approval') }}</p>
    <ErrorBanner v-if="error" :error="error" />
  </article>
</template>

<style scoped>
.music {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  max-width: 480px;
  padding: var(--space-5);
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.names {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.name {
  font: 600 16px/22px var(--font-sans);
}
.by,
.count {
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
.next {
  display: flex;
  flex-direction: column;
  font: 400 14px/20px var(--font-sans);
}
.label {
  color: var(--ink-3);
  font-size: 12px;
}
.controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}
.volume {
  flex: 1 1 120px;
  display: flex;
}
.volume input {
  width: 100%;
  accent-color: var(--accent);
}
.approval {
  color: var(--warning);
  font: 550 14px/20px var(--font-sans);
}
</style>
