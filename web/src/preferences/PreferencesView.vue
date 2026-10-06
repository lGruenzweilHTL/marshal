<script setup lang="ts">
import { Check, Plus, X } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useLocale } from '../i18n/locale'
import { AppButton, Banner, Card, ErrorBanner, Field, LoadBoundary, Page, PageHeader, Segmented, useDesktop } from '../ui'
import { useAppearance, type Appearance } from '../ui/appearance'

/**
 * The user's preferences: language and time zone (applied right after saving, no reload), quiet hours and notification
 * targets. The appearance is per device and never sent to the API.
 */
type Day = Schemas['DayOfWeek']
type Preferences = Schemas['UserPreferencesResponse']

interface QuietWindow {
  key: number
  day: Day
  /** `HH:mm` as the time input speaks it. */
  start: string
  end: string
}

const days: Day[] = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday']
const zones = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []

const api = useApi()
const locale = useLocale()
const desktop = useDesktop()
const { t } = useI18n()
const { appearance, setAppearance } = useAppearance()

const draft = reactive({ language: 'en', timeZone: '', windows: [] as QuietWindow[], person: '', push: '', dndUntil: '' })
const saved = shallowRef(false)
let nextKey = 0

const hhmm = (time: string) => time.slice(0, 5)
const hhmmss = (time: string) => (time.length === 5 ? `${time}:00` : time)
const pad = (value: number) => String(value).padStart(2, '0')

/** ISO instant to a `datetime-local` value in the browser's zone. */
function toLocalInput(iso: string | null): string {
  if (!iso) return ''
  const date = new Date(iso)
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function show(preferences: Preferences) {
  Object.assign(draft, {
    language: preferences.language,
    timeZone: preferences.timeZone,
    windows: preferences.quietHours.map((window) => ({ key: nextKey++, day: window.day, start: hhmm(window.start), end: hhmm(window.end) })),
    person: preferences.notificationTargets.person ?? '',
    push: preferences.notificationTargets.push.join('\n'),
    dndUntil: toLocalInput(preferences.dndUntil),
  })
}

const preferences = useLoad<Preferences>(() => api.call((client) => client.GET('/api/v1/me/preferences')))
watch(preferences.data, (data) => data && show(data), { immediate: true })

const save = useSubmit(async () => {
  saved.value = false
  const result = await api.call((client) =>
    client.PUT('/api/v1/me/preferences', {
      body: {
        language: draft.language,
        timeZone: draft.timeZone,
        quietHours: draft.windows.map((window) => ({ day: window.day, start: hhmmss(window.start), end: hhmmss(window.end) })),
        notificationTargets: {
          person: draft.person.trim() || null,
          push: draft.push
            .split('\n')
            .map((target) => target.trim())
            .filter(Boolean),
        },
        dndUntil: draft.dndUntil ? new Date(draft.dndUntil).toISOString() : null,
      },
    }),
  )
  if (result.data) {
    show(result.data)
    locale.apply(result.data)
    saved.value = true
  }
  return result
})

const languages = [
  { value: 'de', label: 'Deutsch' },
  { value: 'en', label: 'English' },
]
const appearances = computed(() => (['system', 'light', 'dark'] as Appearance[]).map((value) => ({ value, label: t(`preferences.region.appearances.${value}`) })))
const zoneOptions = computed(() => (zones.includes(draft.timeZone) || !draft.timeZone ? zones : [draft.timeZone, ...zones]))
const appearanceModel = computed({ get: () => appearance.value, set: setAppearance })

function addWindow() {
  draft.windows.push({ key: nextKey++, day: 'monday', start: '22:00', end: '07:00' })
}
</script>

<template>
  <Page width="wide">
    <form class="form" @submit.prevent="save.submit()">
      <PageHeader :title="t('preferences.title')" :subtitle="desktop ? t('preferences.subtitle') : undefined">
        <template v-if="desktop && preferences.data.value" #actions>
          <AppButton type="submit" variant="primary" :icon="Check" :loading="save.busy.value">{{ t('preferences.save') }}</AppButton>
        </template>
      </PageHeader>

      <LoadBoundary :load="preferences" size="page">
        <ErrorBanner v-if="save.error.value" :error="save.error.value" />
        <Banner v-if="saved" tone="success" :title="t('preferences.saved')" data-test="saved" />

        <div class="layout" :class="{ two: desktop }">
          <div class="column">
            <Card :title="t('preferences.region.title')">
              <div class="group">
                <span class="t-label">{{ t('preferences.region.language') }}</span>
                <Segmented v-model="draft.language" :options="languages" :label="t('preferences.region.language')" block data-test="language" />
              </div>
              <Field v-model="draft.timeZone" kind="select" name="timeZone" :label="t('preferences.region.timeZone')" :hint="t('preferences.region.timeZoneHint')" required>
                <option v-for="zone in zoneOptions" :key="zone" :value="zone">{{ zone }}</option>
              </Field>
              <div class="group">
                <span class="t-label">{{ t('preferences.region.appearance') }}</span>
                <Segmented v-model="appearanceModel" :options="appearances" :label="t('preferences.region.appearance')" block data-test="appearance" />
                <span class="t-body-s ink-3">{{ t('preferences.region.appearanceHint') }}</span>
              </div>
            </Card>

            <Card :title="t('preferences.notifications.title')">
              <Field v-model="draft.person" name="person" :label="t('preferences.notifications.person')" placeholder="person.name" mono />
              <Field
                v-model="draft.push"
                kind="textarea"
                :rows="3"
                name="push"
                :label="t('preferences.notifications.push')"
                :hint="t('preferences.notifications.pushHint')"
                placeholder="notify.mobile_app_phone"
                mono
              />
              <Field v-model="draft.dndUntil" kind="datetime-local" name="dndUntil" :label="t('preferences.notifications.dnd')" />
              <div v-if="draft.dndUntil">
                <AppButton size="sm" variant="ghost" :icon="X" @click="draft.dndUntil = ''">{{ t('preferences.notifications.dndEnd') }}</AppButton>
              </div>
            </Card>
          </div>

          <Card :title="t('preferences.quiet.title')">
            <p class="t-body-s ink-2">{{ t('preferences.quiet.intro') }}</p>
            <p v-if="!draft.windows.length" class="t-body-s ink-3">{{ t('preferences.quiet.none') }}</p>
            <div v-for="(window, index) in draft.windows" :key="window.key" :data-window="index" class="window">
              <Field v-model="window.day" kind="select" name="day" :label="t('preferences.quiet.day')" :hide-label="index > 0">
                <option v-for="day in days" :key="day" :value="day">{{ t(`preferences.quiet.days.${day}`) }}</option>
              </Field>
              <Field v-model="window.start" kind="time" name="start" :label="t('preferences.quiet.from')" :hide-label="index > 0" required />
              <Field v-model="window.end" kind="time" name="end" :label="t('preferences.quiet.to')" :hide-label="index > 0" required />
              <AppButton variant="ghost" :icon="X" icon-only :label="t('preferences.quiet.remove')" data-test="remove-window" @click="draft.windows.splice(index, 1)" />
            </div>
            <div>
              <AppButton size="sm" :icon="Plus" data-test="add-window" @click="addWindow">{{ t('preferences.quiet.add') }}</AppButton>
            </div>
          </Card>
        </div>

        <AppButton v-if="!desktop" type="submit" variant="primary" size="lg" block :icon="Check" :loading="save.busy.value">{{ t('preferences.save') }}</AppButton>
      </LoadBoundary>
    </form>
  </Page>
</template>

<style scoped>
.form,
.layout,
.column {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.layout.two {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.25fr);
  gap: var(--space-5);
  align-items: start;
}
.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.window {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 136px 136px auto;
  gap: var(--space-2);
  align-items: end;
}
@media (max-width: 899px) {
  .window {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
  }
  .window > :first-child {
    grid-column: 1 / -1;
  }
}
</style>
