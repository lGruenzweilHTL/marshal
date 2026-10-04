<script setup lang="ts">
import { ExternalLink, FileCode2, ListPlus, Pause, Play, Printer, SkipForward, Trash2, X } from 'lucide-vue-next'
import { computed, onUnmounted, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad, type Load } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import {
  AppButton,
  AppIcon,
  Badge,
  Card,
  ErrorBanner,
  IconTile,
  ListCard,
  ListRow,
  LoadBoundary,
  LoadState,
  Page,
  PageHeader,
  Section,
  Segmented,
  useDesktop,
} from '../ui'
import BabysitterCard from './BabysitterCard.vue'
import ConfirmControlDialog from './ConfirmControlDialog.vue'
import ManualControls from './ManualControls.vue'
import PrintHistory from './PrintHistory.vue'
import PrinterCard from './PrinterCard.vue'
import TemperatureChart, { type TemperatureSample } from './TemperatureChart.vue'
import WebcamCard from './WebcamCard.vue'
import { formatTemperature } from './printerFormat'
import { usePrinters } from './usePrinters'
import { usePrinterControls } from './usePrinterControls'

const maxSamples = 120
const babysitterRefresh = 60_000

/** One printer: live state, webcam, temperatures, allowlisted macros and controls, queue, files and history. */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const { load, printers } = usePrinters()
const selected = computed(() => {
  const name = typeof route.params.name === 'string' ? route.params.name : undefined
  return printers.value.find((printer) => printer.name === name) ?? printers.value[0] ?? null
})
const name = computed(() => selected.value?.name ?? '')
const choice = computed({
  get: () => name.value,
  set: (value: string) => void router.push(`/printers/${encodeURIComponent(value)}`),
})
const state = computed(() => (selected.value?.online ? selected.value.state : null))

// Details load per printer; each list fails on its own.
const params = () => ({ path: { printer: name.value } })
const files = shallowRef<Load<Schemas['PrinterFile'][]> | null>(null)
const queue = shallowRef<Load<Schemas['JobQueueStatus']> | null>(null)
const history = shallowRef<Load<Schemas['PrintJob'][]> | null>(null)
function loadDetails() {
  files.value = useLoad(() => api.call((client) => client.GET('/api/v1/printers/{printer}/files', { params: params() })))
  queue.value = useLoad(() => api.call((client) => client.GET('/api/v1/printers/{printer}/queue', { params: params() })), { isEmpty: () => false })
  history.value = useLoad(() => api.call((client) => client.GET('/api/v1/printers/{printer}/history', { params: { ...params(), query: { limit: 10 } } })))
}
watch(name, (value) => value && loadDetails(), { immediate: true })

const controls = usePrinterControls(
  () => name.value,
  () => {
    queue.value?.refresh()
    history.value?.refresh()
  },
)

const samples = shallowRef<TemperatureSample[]>([])
watch(
  state,
  (next) => {
    if (next) samples.value = [...samples.value, { extruder: next.extruder.temperature, bed: next.bed.temperature }].slice(-maxSamples)
  },
  { immediate: true },
)
watch(name, () => (samples.value = []))

// The babysitter checks on its own interval; its verdicts come with the printer list.
const timer = setInterval(() => {
  if (printers.value.some((printer) => printer.babysitter?.active)) load.refresh()
}, babysitterRefresh)
onUnmounted(() => clearInterval(timer))

const size = (bytes: number) => `${format.value.number(Math.round((bytes / 1_048_576) * 10) / 10)} MB`
</script>

<template>
  <Page width="wide">
    <PageHeader v-if="load.state.value !== 'ready'" :title="t('printers.title')" />
    <LoadBoundary :load="load" size="page" :empty-title="t('printers.notConfigured')" :empty-body="t('printers.notConfiguredBody')" :empty-icon="Printer">
      <template v-if="selected">
        <PageHeader :title="selected.name" :subtitle="state ? `Klipper ${state.klippyState}` : t('printers.status.offline')">
          <template v-if="printers.length > 1" #actions>
            <Segmented v-model="choice" :options="printers.map((printer) => ({ value: printer.name, label: printer.name }))" :label="t('printers.choose')" data-test="printers" />
          </template>
        </PageHeader>

        <div class="columns" :class="{ split: desktop }">
          <div class="column">
            <PrinterCard :key="selected.name" :printer="selected" actions />
            <WebcamCard :key="`cam-${selected.name}`" :printer="selected.name" />
            <Section :title="t('printers.temperature')">
              <Card data-test="temperatures">
                <TemperatureChart :samples="samples" :targets="state ? { extruder: state.extruder.target, bed: state.bed.target } : undefined" />
                <div v-if="state" class="legend">
                  <span class="key"><span class="dot hotend" />{{ t('printers.temperatureNow', { heater: t('printers.hotend'), value: formatTemperature(state.extruder) }) }}</span>
                  <span class="key"><span class="dot bed" />{{ t('printers.temperatureNow', { heater: t('printers.bed'), value: formatTemperature(state.bed) }) }}</span>
                </div>
              </Card>
            </Section>
            <BabysitterCard v-if="selected.babysitter" :babysitter="selected.babysitter" />
            <Section v-if="desktop" :title="t('printers.history')" data-test="history">
              <PrintHistory v-if="history" :load="history" />
            </Section>
          </div>

          <div class="column">
            <Section :title="t('printers.macros')">
              <div v-if="selected.macros.length" class="macros">
                <button
                  v-for="macro in selected.macros"
                  :key="macro"
                  type="button"
                  class="macro"
                  data-control="macro"
                  :disabled="controls.busy.value"
                  @click="controls.send({ action: 'run_macro', macro })"
                >
                  <AppIcon :icon="Play" :size="16" />
                  <span>{{ macro }}</span>
                </button>
              </div>
              <p class="note">
                {{ t('printers.macrosNote') }} {{ t('printers.elsewhere') }}
                <a v-if="selected.webUrl" data-test="web-ui" :href="selected.webUrl" target="_blank" rel="noopener noreferrer">
                  {{ t('printers.webUi') }} <AppIcon :icon="ExternalLink" :size="14" />
                </a>
              </p>
            </Section>
            <ErrorBanner v-if="controls.error.value" :error="controls.error.value" />
            <ManualControls :controls="controls" />

            <Section :title="t('printers.queue')" :count="queue?.data.value?.jobs.length" data-test="queue">
              <template #actions>
                <Badge v-if="queue?.data.value" :label="queue.data.value.state" />
              </template>
              <LoadBoundary v-if="queue" :load="queue">
                <template #default="{ data }">
                  <ListCard>
                    <ListRow v-for="job in data.jobs" :key="job.jobId" :chevron="false">
                      <template #lead><IconTile :icon="FileCode2" /></template>
                      <template #title><span class="t-mono-s">{{ job.filename }}</span></template>
                      <template #subtitle><span class="num">{{ t('printers.added', { time: format.dateTime(job.addedAt) }) }}</span></template>
                      <template #trail>
                        <span class="row-actions">
                          <AppButton size="sm" variant="ghost" :icon="SkipForward" icon-only :label="t('printers.printNext')" @click="controls.send({ action: 'queue_jump', jobId: job.jobId })" />
                          <AppButton size="sm" variant="ghost" :icon="X" icon-only :label="t('printers.remove')" @click="controls.send({ action: 'queue_remove', jobIds: [job.jobId] })" />
                        </span>
                      </template>
                    </ListRow>
                    <LoadState v-if="data.jobs.length === 0" state="empty" :title="t('printers.emptyQueue')" />
                    <template #footer>
                      <div class="queue-actions">
                        <AppButton size="sm" :icon="Play" @click="controls.send({ action: 'queue_start' })">{{ t('printers.queueStart') }}</AppButton>
                        <AppButton size="sm" :icon="Pause" @click="controls.send({ action: 'queue_pause' })">{{ t('printers.queuePause') }}</AppButton>
                        <AppButton size="sm" variant="ghost" :icon="Trash2" @click="controls.send({ action: 'queue_clear' })">{{ t('printers.queueClear') }}</AppButton>
                      </div>
                    </template>
                  </ListCard>
                </template>
              </LoadBoundary>
            </Section>

            <Section :title="t('printers.files')">
              <LoadBoundary v-if="files" :load="files" :empty-title="t('printers.noFiles')">
                <template #default="{ data }">
                  <ListCard>
                    <ListRow v-for="file in data" :key="file.path" :chevron="false" :data-file="file.path">
                      <template #lead><IconTile :icon="FileCode2" /></template>
                      <template #title><span class="t-mono-s">{{ file.path }}</span></template>
                      <template #subtitle><span class="num">{{ size(file.size) }} · {{ format.dateTime(file.modified) }}</span></template>
                      <template #trail>
                        <span class="row-actions">
                          <AppButton size="sm" variant="ghost" :icon="ListPlus" icon-only :label="t('printers.addToQueue')" @click="controls.send({ action: 'queue_add', files: [file.path] })" />
                          <AppButton size="sm" :icon="Play" data-control="print" @click="controls.send({ action: 'start_print', file: file.path })">{{ t('printers.print') }}</AppButton>
                        </span>
                      </template>
                    </ListRow>
                  </ListCard>
                </template>
              </LoadBoundary>
            </Section>

            <Section v-if="!desktop" :title="t('printers.history')" data-test="history">
              <PrintHistory v-if="history" :load="history" />
            </Section>
          </div>
        </div>
        <ConfirmControlDialog :controls="controls" />
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.columns,
.column {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.columns.split {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 28px;
  align-items: start;
}
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  margin-top: var(--space-3);
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
.key {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.dot {
  width: 10px;
  height: 10px;
  border-radius: var(--radius-full);
}
.dot.hotend {
  background: var(--accent);
}
.dot.bed {
  background: var(--info);
}
.macros {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-2);
}
.macro {
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 48px;
  padding: 0 14px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--ink);
  font: var(--text-mono-s);
  cursor: pointer;
  transition: background var(--transition);
}
.macro:hover {
  background: var(--surface-2);
}
.macro:focus-visible {
  outline: var(--focus-ring);
}
.macro:disabled {
  opacity: 0.5;
  cursor: default;
}
.note {
  margin-top: var(--space-3);
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
.row-actions {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}
.queue-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>
