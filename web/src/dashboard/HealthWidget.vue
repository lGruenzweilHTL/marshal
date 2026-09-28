<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useApi } from '../api/inject'
import HealthChecks from '../system/HealthChecks.vue'
import { loadHealth, type HealthReport } from '../system/health'

const session = useApi()
const report = ref<HealthReport | null>(null)
const failed = ref(false)

onMounted(async () => {
  try {
    report.value = await loadHealth(session)
  } catch {
    failed.value = true
  }
})
</script>

<template>
  <h2><RouterLink to="/system">Health</RouterLink></h2>
  <HealthChecks v-if="report" :report="report" />
  <p v-if="failed" class="error">Marshal did not answer the health check.</p>
</template>
