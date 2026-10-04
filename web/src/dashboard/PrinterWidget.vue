<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { useLiveEvent } from '../live/liveEvents'
import { formatEta, formatPercent, formatTemperature } from '../printers/printerFormat'

const session = useApiSession()
const printers = ref<Schemas['PrinterSummary'][]>([])

onMounted(async () => {
  const { data } = await session.raw.GET('/api/v1/printers')
  printers.value = data ?? []
})

useLiveEvent('printer', 'printer.state', ({ printer, state }) => {
  const summary = printers.value.find((candidate) => candidate.name === printer)
  if (!summary) return
  summary.online = state !== null
  summary.state = state
})
</script>

<template>
  <h2><RouterLink to="/printers">Printer</RouterLink></h2>
  <ul class="plain">
    <li v-for="printer in printers" :key="printer.name">
      <strong>{{ printer.name }}</strong>
      <template v-if="printer.online && printer.state">
        · {{ printer.state.printState }}
        <template v-if="printer.state.printState === 'printing' || printer.state.printState === 'paused'">
          · {{ formatPercent(printer.state.progress) }}<template v-if="printer.state.etaSeconds !== null"> · {{ formatEta(printer.state.etaSeconds) }} left</template>
        </template>
        <br />
        <span class="muted">Hotend {{ formatTemperature(printer.state.extruder) }} · Bed {{ formatTemperature(printer.state.bed) }}</span>
      </template>
      <span v-else class="muted"> · offline</span>
    </li>
  </ul>
  <p v-if="printers.length === 0" class="muted">No printer configured.</p>
</template>
