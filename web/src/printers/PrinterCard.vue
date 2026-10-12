<script setup lang="ts">
import { AlertTriangle, CircleStop, Pause, Play, Printer, Thermometer, WifiOff } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { AppButton, AppIcon, Badge, ErrorBanner, IconTile, ProgressBar } from '../ui'
import ConfirmControlDialog from './ConfirmControlDialog.vue'
import { formatEta, formatTemperature } from './printerFormat'
import { printerStatus, statusTone } from './printerStatus'
import { usePrinterControls } from './usePrinterControls'

/**
 * One printer at a glance: state, file, progress with layer and time left; `full` adds the temperatures and, with
 * `actions`, pause / resume / cancel (cancel and resume are confirm tier and ask first).
 */
const props = withDefaults(defineProps<{ printer: Schemas['PrinterSummary']; density?: 'full' | 'compact'; actions?: boolean }>(), {
  density: 'full',
  actions: false,
})
const { t } = useI18n()
const controls = usePrinterControls(() => props.printer.name)

const status = computed(() => printerStatus(props.printer))
const state = computed(() => (status.value === 'offline' ? null : props.printer.state))
const full = computed(() => props.density === 'full')
const running = computed(() => status.value === 'printing' || status.value === 'paused' || (status.value === 'error' && !!state.value?.filename))
const percent = computed(() => Math.round((state.value?.progress ?? 0) * 100))
const message = computed(() => {
  if (status.value === 'offline') return t('printers.offlineText')
  if (status.value === 'error') return state.value?.message || state.value?.stateMessage || t('printers.status.error')
  if (status.value === 'paused') return state.value?.message ?? ''
  if (status.value === 'idle') return t('printers.idleText')
  if (status.value === 'complete') return t('printers.completeText')
  return ''
})
const messageIcon = computed(() => (status.value === 'offline' ? WifiOff : status.value === 'error' || status.value === 'paused' ? AlertTriangle : Printer))
const alert = computed(() => status.value === 'error' || status.value === 'paused')
const barTone = computed(() => (status.value === 'error' ? 'danger' : status.value === 'paused' ? 'warning' : 'accent'))
</script>

<template>
  <article class="printer" :class="[density, status]" :data-printer="printer.name">
    <div class="head">
      <IconTile :icon="Printer" :size="40" />
      <span class="names">
        <span class="name">{{ printer.name }}</span>
        <span v-if="running && state?.filename" class="file">{{ state.filename }}</span>
      </span>
      <Badge :tone="statusTone[status]" dot :label="t(`printers.status.${status}`)" />
    </div>

    <div v-if="message" class="message" :role="alert ? 'alert' : undefined">
      <AppIcon :icon="messageIcon" :size="18" />
      <span>{{ message }}</span>
    </div>

    <template v-if="running && state">
      <div class="numbers">
        <span class="big num" data-test="progress">{{ percent }}<span class="unit">%</span></span>
        <span class="side">
          <span v-if="state.currentLayer !== null && state.totalLayers" class="num">{{ t('printers.layer', { layer: `${state.currentLayer} / ${state.totalLayers}` }) }}</span>
          <span v-if="state.etaSeconds !== null" class="eta num">{{ t('printers.left', { time: formatEta(state.etaSeconds) }) }}</span>
        </span>
      </div>
      <ProgressBar :value="percent" :label="t('printers.progress')" :tone="barTone" />
    </template>

    <template v-if="full && state">
      <div class="heaters">
        <div class="heater">
          <AppIcon :icon="Thermometer" :size="18" />
          <span class="reading"><span class="label">{{ t('printers.hotend') }}</span><span class="num value">{{ formatTemperature(state.extruder) }}</span></span>
        </div>
        <div class="heater">
          <AppIcon :icon="Thermometer" :size="18" />
          <span class="reading"><span class="label">{{ t('printers.bed') }}</span><span class="num value">{{ formatTemperature(state.bed) }}</span></span>
        </div>
      </div>
      <div v-if="actions && running" class="actions">
        <AppButton
          v-if="status === 'printing'"
          :icon="Pause"
          data-control="pause"
          :disabled="controls.busy.value"
          @click="controls.send({ action: 'pause' })"
        >{{ t('printers.pause') }}</AppButton>
        <AppButton v-else :icon="Play" data-control="resume" :disabled="controls.busy.value" @click="controls.send({ action: 'resume' })">{{ t('printers.resume') }}</AppButton>
        <AppButton variant="ghost" :icon="CircleStop" data-control="cancel" :disabled="controls.busy.value" @click="controls.send({ action: 'cancel' })">
          {{ t('printers.cancel') }}
        </AppButton>
      </div>
      <ErrorBanner v-if="controls.error.value" :error="controls.error.value" />
      <ConfirmControlDialog :controls="controls" />
    </template>
  </article>
</template>

<style scoped>
.printer {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-5);
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
}
.compact {
  padding: var(--space-4);
  gap: var(--space-3);
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.names {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  min-width: 0;
}
.name {
  font: 600 16px/22px var(--font-sans);
}
.file {
  overflow: hidden;
  color: var(--ink-3);
  font: 450 12px/16px var(--font-mono);
  white-space: nowrap;
  text-overflow: ellipsis;
}
.message {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  padding: var(--space-3) 14px;
  border-radius: var(--radius-sm);
  background: var(--success-soft);
  color: var(--success);
  font: 550 14px/20px var(--font-sans);
}
.error .message {
  background: var(--danger-soft);
  color: var(--danger);
}
.paused .message {
  background: var(--warning-soft);
  color: var(--warning);
}
.offline .message {
  background: var(--surface-3);
  color: var(--ink-2);
}
.numbers {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--space-3);
}
.big {
  font: 650 40px/44px var(--font-display);
  letter-spacing: -0.03em;
}
.compact .big {
  font-size: 30px;
  line-height: 34px;
}
.unit {
  margin-left: 2px;
  font-size: 0.5em;
  color: var(--ink-3);
}
.side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
.eta {
  font-weight: 600;
  color: var(--ink);
}
.heaters {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
.heater {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--ink-3);
}
.reading {
  display: flex;
  flex-direction: column;
}
.label {
  font-size: 12px;
}
.value {
  font-weight: 600;
  color: var(--ink);
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
</style>
