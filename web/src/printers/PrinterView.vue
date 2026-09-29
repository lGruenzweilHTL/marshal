<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'
import TemperatureChart, { type TemperatureSample } from './TemperatureChart.vue'
import { formatEta, formatPercent, formatTemperature } from './printerFormat'

type ControlRequest = Schemas['PrinterControlRequest']

const maxSamples = 120

const session = useApi()
const route = useRoute()
const printers = ref<Schemas['PrinterSummary'][] | null>(null)
const files = ref<Schemas['PrinterFile'][]>([])
const queue = ref<Schemas['JobQueueStatus'] | null>(null)
const history = ref<Schemas['PrintJob'][]>([])
const samples = ref<TemperatureSample[]>([])
const snapshotUrl = ref<string | null>(null)
const pending = ref<{ description: string; token: string } | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)
const inputs = reactive({ extruderTarget: '', bedTarget: '', axis: 'z', distance: '10' })

const selected = computed(() => {
  const name = typeof route.params.name === 'string' ? route.params.name : undefined
  return printers.value?.find((printer) => printer.name === name) ?? printers.value?.[0] ?? null
})
const state = computed(() => (selected.value?.online ? selected.value.state : null))
const path = computed(() => ({ printer: selected.value?.name ?? '' }))
const streamUrl = computed(() => `${session.baseUrl}/api/v1/printers/${encodeURIComponent(path.value.printer)}/webcam/stream`)
const active = computed(() => state.value?.printState === 'printing' || state.value?.printState === 'paused')

function record(sample: Schemas['PrinterState'] | null) {
  if (!sample) return
  samples.value = [...samples.value, { extruder: sample.extruder.temperature, bed: sample.bed.temperature }].slice(-maxSamples)
}

async function loadPrinters() {
  const { data } = await session.api.GET('/api/v1/printers')
  printers.value = data ?? []
  record(state.value)
}

async function loadDetails() {
  if (!selected.value) return
  const params = { path: path.value }
  const [fileList, jobQueue, jobs] = await Promise.all([
    session.api.GET('/api/v1/printers/{printer}/files', { params }),
    session.api.GET('/api/v1/printers/{printer}/queue', { params }),
    session.api.GET('/api/v1/printers/{printer}/history', { params: { ...params, query: { limit: 10 } } }),
  ])
  files.value = fileList.data ?? []
  queue.value = jobQueue.data ?? null
  history.value = jobs.data ?? []
}

async function control(body: ControlRequest) {
  error.value = null
  busy.value = true
  const { data, error: problem } = await session.api.POST('/api/v1/printers/{printer}/controls', { params: { path: path.value }, body })
  busy.value = false
  if (!data) {
    error.value = problemMessage(problem, 'The printer did not accept the command.')
    return
  }
  if (data.status === 'confirmation_required' && data.token) pending.value = { description: data.description, token: data.token }
  else await loadDetails()
}

async function confirm() {
  const token = pending.value?.token
  pending.value = null
  if (!token) return
  const { data, error: problem } = await session.api.POST('/api/v1/printers/{printer}/controls/confirm', { params: { path: path.value }, body: { token } })
  if (!data) error.value = problemMessage(problem, 'The command was not carried out.')
  else await loadDetails()
}

function setTemperature(heater: 'extruder' | 'bed') {
  const target = Number(heater === 'extruder' ? inputs.extruderTarget : inputs.bedTarget)
  void control({ action: 'set_temperature', heater, target })
}

async function snapshot() {
  const { data, error: problem } = await session.api.POST('/api/v1/printers/{printer}/snapshots', { params: { path: path.value } })
  if (data) snapshotUrl.value = data.url
  else error.value = problemMessage(problem, 'No snapshot could be taken.')
}

useLiveEvent('printer', 'printer.state', ({ printer, state: next }) => {
  const summary = printers.value?.find((candidate) => candidate.name === printer)
  if (!summary) return
  summary.online = next !== null
  summary.state = next
  if (summary === selected.value) record(next)
})

watch(
  () => selected.value?.name,
  () => void loadDetails(),
)

void loadPrinters()
</script>

<template>
  <section class="page">
    <h1>Printer</h1>
    <p v-if="printers && printers.length === 0" class="muted">No printer is configured.</p>
    <template v-else-if="selected">
      <nav v-if="printers && printers.length > 1" class="tabs">
        <RouterLink v-for="printer in printers" :key="printer.name" :to="`/printers/${printer.name}`">{{ printer.name }}</RouterLink>
      </nav>
      <p v-if="error" role="alert" class="error">{{ error }}</p>

      <div class="grid">
        <article class="card" data-test="status">
          <h2>{{ selected.name }}</h2>
          <template v-if="state">
            <p>
              <strong>{{ state.printState }}</strong>
              <span v-if="state.filename && active"> · {{ state.filename }}</span>
              <span v-if="state.klippyState !== 'ready'"> · Klipper {{ state.klippyState }}<template v-if="state.stateMessage"> ({{ state.stateMessage }})</template></span>
            </p>
            <template v-if="active">
              <progress max="1" :value="state.progress" />
              <p>
                {{ formatPercent(state.progress) }}
                <span v-if="state.etaSeconds !== null"> · {{ formatEta(state.etaSeconds) }} left</span>
                <span v-if="state.currentLayer !== null && state.totalLayers !== null"> · Layer {{ state.currentLayer }} / {{ state.totalLayers }}</span>
              </p>
            </template>
            <p v-if="state.message" class="muted">{{ state.message }}</p>
          </template>
          <p v-else class="muted">The printer is offline.</p>
        </article>

        <article class="card" data-test="temperatures">
          <h2>Temperatures</h2>
          <template v-if="state">
            <p>Hotend {{ formatTemperature(state.extruder) }} · Bed {{ formatTemperature(state.bed) }}</p>
          </template>
          <TemperatureChart :samples="samples" />
        </article>

        <article class="card">
          <h2>Webcam</h2>
          <img data-test="webcam" class="webcam" :src="streamUrl" alt="Webcam stream" />
          <button type="button" @click="snapshot">Snapshot</button>
          <img v-if="snapshotUrl" class="webcam" :src="snapshotUrl" alt="Webcam snapshot" />
        </article>

        <article class="card">
          <h2>Controls</h2>
          <div class="row">
            <button type="button" data-control="pause" :disabled="busy" @click="control({ action: 'pause' })">Pause</button>
            <button type="button" data-control="resume" :disabled="busy" @click="control({ action: 'resume' })">Resume</button>
            <button type="button" data-control="cancel" :disabled="busy" @click="control({ action: 'cancel' })">Cancel</button>
            <button type="button" class="danger" data-control="emergency_stop" :disabled="busy" @click="control({ action: 'emergency_stop' })">Emergency stop</button>
          </div>
          <div class="row">
            <label>Hotend °C <input v-model="inputs.extruderTarget" name="extruderTarget" type="number" min="0" /></label>
            <button type="button" data-control="extruder" @click="setTemperature('extruder')">Set</button>
            <label>Bed °C <input v-model="inputs.bedTarget" name="bedTarget" type="number" min="0" /></label>
            <button type="button" data-control="bed" @click="setTemperature('bed')">Set</button>
          </div>
          <div class="row">
            <button type="button" data-control="home" @click="control({ action: 'home' })">Home all</button>
            <label>Axis
              <select v-model="inputs.axis" name="axis">
                <option value="x">X</option>
                <option value="y">Y</option>
                <option value="z">Z</option>
              </select>
            </label>
            <label>mm <input v-model="inputs.distance" name="distance" type="number" step="0.1" /></label>
            <button type="button" data-control="move" @click="control({ action: 'move', axis: inputs.axis, distance: Number(inputs.distance) })">Move</button>
          </div>
          <div v-if="selected.macros.length > 0" class="row">
            <button v-for="macro in selected.macros" :key="macro" type="button" data-control="macro" @click="control({ action: 'run_macro', macro })">{{ macro }}</button>
          </div>
          <p class="muted">
            Firmware restarts, G-code and file management are not available here<template v-if="selected.webUrl">; use
              <a data-test="web-ui" :href="selected.webUrl" target="_blank" rel="noopener noreferrer">the printer's own interface</a></template>.
          </p>
        </article>

        <article class="card">
          <h2>Files</h2>
          <ul class="plain">
            <li v-for="file in files" :key="file.path" :data-file="file.path">
              {{ file.path }}
              <button type="button" @click="control({ action: 'start_print', file: file.path })">Print</button>
              <button type="button" @click="control({ action: 'queue_add', files: [file.path] })">Queue</button>
            </li>
          </ul>
          <p v-if="files.length === 0" class="muted">No files.</p>
        </article>

        <article class="card" data-test="queue">
          <h2>Queue <span v-if="queue" class="muted">({{ queue.state }})</span></h2>
          <ul class="plain">
            <li v-for="job in queue?.jobs ?? []" :key="job.jobId">
              {{ job.filename }}
              <button type="button" @click="control({ action: 'queue_jump', jobId: job.jobId })">Next</button>
              <button type="button" @click="control({ action: 'queue_remove', jobIds: [job.jobId] })">Remove</button>
            </li>
          </ul>
          <div class="row">
            <button type="button" @click="control({ action: 'queue_start' })">Start queue</button>
            <button type="button" @click="control({ action: 'queue_pause' })">Pause queue</button>
            <button type="button" @click="control({ action: 'queue_clear' })">Clear</button>
          </div>
        </article>

        <article class="card" data-test="history">
          <h2>History</h2>
          <ul class="plain">
            <li v-for="job in history" :key="job.jobId">{{ formatDateTime(job.startedAt) }} · {{ job.filename }} · {{ job.status }}</li>
          </ul>
          <p v-if="history.length === 0" class="muted">No prints yet.</p>
        </article>
      </div>

      <div v-if="pending" class="backdrop">
        <div role="dialog" aria-modal="true" aria-labelledby="confirm-title" class="dialog">
          <h2 id="confirm-title">Confirm</h2>
          <p>{{ selected.name }}: {{ pending.description }}?</p>
          <div class="row">
            <button type="button" data-test="confirm" @click="confirm">Confirm</button>
            <button type="button" data-test="cancel" @click="pending = null">Cancel</button>
          </div>
        </div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 20rem), 1fr));
  gap: 1rem;
}

.card {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 12px;
  min-width: 0;
}

.card h2 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: end;
  margin-block: 0.5rem;
}

.row input {
  width: 5rem;
}

progress {
  width: 100%;
}

.webcam {
  display: block;
  width: 100%;
  border-radius: 8px;
  margin-bottom: 0.5rem;
  background: var(--border);
}

.tabs {
  display: flex;
  gap: 1rem;
  margin-bottom: 1rem;
}

.danger {
  border-color: var(--error);
  color: var(--error);
}

.backdrop {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: rgb(0 0 0 / 40%);
}

.dialog {
  max-width: min(90vw, 24rem);
  padding: 1.5rem;
  border-radius: 12px;
  background: var(--bg);
}
</style>
