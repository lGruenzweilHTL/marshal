<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useApiSession } from '../api/inject'
import HealthChecks from '../system/HealthChecks.vue'
import { loadHealth, type HealthReport } from '../system/health'

const session = useApiSession()
const report = ref<HealthReport | null>(null)
const failed = ref(false)

onMounted(async () => {
  const result = await loadHealth(session)
  if (result.error) failed.value = true
  else report.value = result.data
})
</script>

<template>
  <h2><RouterLink to="/system">Health</RouterLink></h2>
  <HealthChecks v-if="report" :report="report" />
  <p v-if="failed" class="error">Marshal did not answer the health check.</p>
</template>
