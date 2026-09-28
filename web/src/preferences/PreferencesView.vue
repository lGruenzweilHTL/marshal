<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'

type Day = Schemas['DayOfWeek']

interface Window {
  key: number
  day: Day
  /** `HH:mm` as the time input speaks it. */
  start: string
  end: string
}

const days: Day[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday']
const timeZones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []

const session = useApi()
const loaded = ref(false)
const error = ref<string | null>(null)
const saved = ref(false)
const language = ref('en')
const timeZone = ref('')
const windows = ref<Window[]>([])
const person = ref('')
const push = ref('')
const dndUntil = ref('')
let nextKey = 0

const hhmm = (time: string) => time.slice(0, 5)
const hhmmss = (time: string) => (time.length === 5 ? `${time}:00` : time)
const pad = (value: number) => String(value).padStart(2, '0')

/** ISO instant → `datetime-local` value in the browser's zone. */
function toLocalInput(iso: string | null): string {
  if (!iso) return ''
  const date = new Date(iso)
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function show(preferences: Schemas['UserPreferencesResponse']) {
  language.value = preferences.language
  timeZone.value = preferences.timeZone
  windows.value = preferences.quietHours.map((window) => ({ key: nextKey++, day: window.day ?? 'monday', start: hhmm(window.start), end: hhmm(window.end) }))
  person.value = preferences.notificationTargets.person ?? ''
  push.value = preferences.notificationTargets.push.join('\n')
  dndUntil.value = toLocalInput(preferences.dndUntil)
}

function addWindow() {
  windows.value.push({ key: nextKey++, day: 'monday', start: '22:00', end: '07:00' })
}

async function save() {
  error.value = null
  saved.value = false
  const { data, error: problem } = await session.api.PUT('/api/v1/me/preferences', {
    body: {
      language: language.value,
      timeZone: timeZone.value.trim(),
      quietHours: windows.value.map((window) => ({ day: window.day, start: hhmmss(window.start), end: hhmmss(window.end) })),
      notificationTargets: {
        person: person.value.trim() || null,
        push: push.value
          .split('\n')
          .map((target) => target.trim())
          .filter(Boolean),
      },
      dndUntil: dndUntil.value ? new Date(dndUntil.value).toISOString() : null,
    },
  })
  if (!data) {
    error.value = problemMessage(problem, 'The preferences could not be saved.')
    return
  }
  show(data)
  saved.value = true
}

onMounted(async () => {
  const { data } = await session.api.GET('/api/v1/me/preferences')
  if (data) {
    show(data)
    loaded.value = true
  } else error.value = 'The preferences could not be loaded.'
})
</script>

<template>
  <section class="page">
    <h1>Preferences</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <form v-if="loaded" class="stack" @submit.prevent="save">
      <label
        >Language
        <select v-model="language" name="language">
          <option value="en">English</option>
          <option value="de">Deutsch</option>
        </select>
      </label>
      <label
        >Time zone
        <input v-model="timeZone" name="timeZone" list="time-zones" autocomplete="off" required />
        <datalist id="time-zones">
          <option v-for="zone in timeZones" :key="zone" :value="zone" />
        </datalist>
      </label>

      <fieldset class="stack">
        <legend>Quiet hours</legend>
        <p class="muted">Marshal holds back non-critical notifications during these windows. A window may run past midnight.</p>
        <div v-for="(window, index) in windows" :key="window.key" :data-window="index" class="window">
          <select v-model="window.day" aria-label="Day">
            <option v-for="day in days" :key="day" :value="day">{{ day }}</option>
          </select>
          <input v-model="window.start" name="start" type="time" aria-label="From" required />
          <input v-model="window.end" name="end" type="time" aria-label="Until" required />
          <button type="button" data-test="remove-window" aria-label="Remove window" @click="windows.splice(index, 1)">✕</button>
        </div>
        <button type="button" data-test="add-window" @click="addWindow">Add window</button>
      </fieldset>

      <fieldset class="stack">
        <legend>Notifications</legend>
        <label>Home Assistant person <input v-model="person" name="person" placeholder="person.name" /></label>
        <label>Push targets (one per line) <textarea v-model="push" name="push" rows="3" placeholder="notify.mobile_app_phone"></textarea></label>
        <label>Do not disturb until <input v-model="dndUntil" name="dndUntil" type="datetime-local" /></label>
        <button v-if="dndUntil" type="button" @click="dndUntil = ''">End do-not-disturb</button>
      </fieldset>

      <button type="submit">Save</button>
      <p v-if="saved" role="status">Saved.</p>
    </form>
  </section>
</template>

<style scoped>
fieldset {
  border: 1px solid var(--border);
  border-radius: 8px;
}

fieldset p {
  margin: 0;
}

.window {
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) minmax(0, 1fr) auto;
  gap: 0.5rem;
}

@media (max-width: 480px) {
  .window {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
  }

  .window select {
    grid-column: 1 / -1;
  }
}
</style>
