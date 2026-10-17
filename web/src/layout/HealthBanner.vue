<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLiveEvent, useLiveReconnect } from '../live/liveEvents'
import { useUserSession } from '../session'
import Banner from '../ui/Banner.vue'

/**
 * Health warnings at the top of every page: the highest severity's color, the count and the first message. Dismissing
 * hides it until the set of warning codes changes (kept per browser).
 */
const api = useApi()
const user = useUserSession()
const { t, locale } = useI18n()

const dismissedKey = 'marshal.health.dismissed'
const warnings = shallowRef<Schemas['HealthWarningView'][]>([])
const dismissed = shallowRef(readDismissed())

const codes = computed(() => [...new Set(warnings.value.map((warning) => warning.code))].sort().join(','))
const visible = computed(() => warnings.value.length > 0 && dismissed.value !== codes.value)
const tone = computed(() => (warnings.value.some((warning) => warning.severity === 'error') ? 'danger' : 'warning'))
const first = computed(() => warnings.value[0])
const message = computed(() => (first.value ? (locale.value === 'de' ? first.value.message.de : first.value.message.en) : ''))
const admin = computed(() => user.current.value?.roles.includes('admin') ?? false)
// A single warning's own link; with several, the system view lists them all.
const ownLink = computed(() => (admin.value && warnings.value.length === 1 ? first.value?.link : null))

function readDismissed(): string | null {
  try {
    return localStorage.getItem(dismissedKey)
  } catch {
    return null
  }
}

function dismiss() {
  dismissed.value = codes.value
  try {
    localStorage.setItem(dismissedKey, codes.value)
  } catch {
    // Without storage the banner stays dismissed until the page reloads.
  }
}

async function load() {
  const result = await api.call((client) => client.GET('/api/v1/system/health'))
  // The banner is a hint: a failed read shows nothing, the system view shows the error.
  if (!result.error) warnings.value = result.data.warnings
}

useLiveEvent('health', 'health.changed', (payload) => (warnings.value = payload.warnings))
// Changes while the stream was down are missed; read the current state again.
useLiveReconnect(() => void load())
void load()
</script>

<template>
  <div v-if="visible" class="health-banner" data-test="health-banner" :class="tone">
    <Banner :tone="tone" :title="t('shell.health.title', warnings.length)" :body="message" dismissible @dismiss="dismiss">
      <template #action>
        <span class="links">
          <RouterLink v-if="ownLink" :to="ownLink" class="link" data-test="health-open">{{ t('shell.health.open') }}</RouterLink>
          <RouterLink to="/system" class="link" data-test="health-details">{{ t('shell.health.details') }}</RouterLink>
        </span>
      </template>
    </Banner>
  </div>
</template>

<style scoped>
.health-banner {
  padding: var(--space-3) var(--page-gutter) 0;
}
.links {
  display: flex;
  gap: var(--space-3);
}
.link {
  font-weight: 600;
  color: var(--ink);
}
</style>
