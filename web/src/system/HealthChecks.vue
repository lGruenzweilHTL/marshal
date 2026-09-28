<script setup lang="ts">
import type { HealthReport } from './health'

defineProps<{ report: HealthReport; details?: boolean }>()
</script>

<template>
  <div class="health">
    <p>
      Status: <strong :class="['badge', report.status]">{{ report.status }}</strong>
    </p>
    <ul class="checks">
      <template v-for="(check, name) in report.checks" :key="name">
        <li v-if="details || check.status !== 'healthy'" :data-check="name">
          <span>{{ name }}</span> <span :class="['badge', check.status]">{{ check.status }}</span>
          <span v-if="check.description"> – {{ check.description }}</span>
          <ul v-if="check.warnings?.length">
            <li v-for="warning in check.warnings" :key="warning" class="warning">{{ warning }}</li>
          </ul>
        </li>
      </template>
    </ul>
  </div>
</template>

<style scoped>
.checks {
  margin: 0;
  padding-left: 1.25rem;
}

.warning {
  overflow-wrap: anywhere;
}
</style>
