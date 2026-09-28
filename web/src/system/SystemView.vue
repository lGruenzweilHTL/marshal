<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import HealthChecks from './HealthChecks.vue'
import { loadHealth, type HealthReport } from './health'

const session = useApi()
const info = ref<Schemas['SystemInfo'] | null>(null)
const report = ref<HealthReport | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    const [system, healthReport] = await Promise.all([session.api.GET('/api/v1/system'), loadHealth(session)])
    info.value = system.data ?? null
    report.value = healthReport
  } catch {
    error.value = 'The system information could not be loaded.'
  }
})
</script>

<template>
  <section class="page">
    <h1>System</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <dl v-if="info" class="facts">
      <dt>Version</dt>
      <dd data-test="version">{{ info.version }}</dd>
      <dt>API versions</dt>
      <dd data-test="api-versions">
        {{ info.supportedApiVersions.join(', ') }}
        <template v-if="info.deprecatedApiVersions.length"> (deprecated: {{ info.deprecatedApiVersions.join(', ') }})</template>
      </dd>
    </dl>
    <h2>Health</h2>
    <HealthChecks v-if="report" :report="report" details />
  </section>
</template>
