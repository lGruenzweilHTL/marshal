<script setup lang="ts">
import { Home, Move, OctagonX } from 'lucide-vue-next'
import { reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { AppButton, Card, Field } from '../ui'
import type { PrinterControls } from './usePrinterControls'

/** Heater targets, homing, jogging and the emergency stop; confirm-tier ones go through the controls' dialog. */
const props = defineProps<{ controls: PrinterControls }>()
const { t } = useI18n()
const inputs = reactive({ extruder: '', bed: '', axis: 'z', distance: '10' })

function setTemperature(heater: 'extruder' | 'bed') {
  void props.controls.send({ action: 'set_temperature', heater, target: Number(inputs[heater]) })
}
</script>

<template>
  <Card :title="t('printers.controls')">
    <div class="rows">
      <div class="row">
        <Field v-model="inputs.extruder" kind="number" name="extruderTarget" :label="t('printers.target', { heater: t('printers.hotend') })" />
        <AppButton data-control="extruder" :disabled="!inputs.extruder || controls.busy.value" @click="setTemperature('extruder')">{{ t('printers.set') }}</AppButton>
      </div>
      <div class="row">
        <Field v-model="inputs.bed" kind="number" name="bedTarget" :label="t('printers.target', { heater: t('printers.bed') })" />
        <AppButton data-control="bed" :disabled="!inputs.bed || controls.busy.value" @click="setTemperature('bed')">{{ t('printers.set') }}</AppButton>
      </div>
      <div class="row">
        <Field v-model="inputs.axis" kind="select" name="axis" :label="t('printers.axis')">
          <option value="x">X</option>
          <option value="y">Y</option>
          <option value="z">Z</option>
        </Field>
        <Field v-model="inputs.distance" kind="number" name="distance" :label="t('printers.distance')" />
        <AppButton
          :icon="Move"
          data-control="move"
          :disabled="controls.busy.value"
          @click="controls.send({ action: 'move', axis: inputs.axis, distance: Number(inputs.distance) })"
        >{{ t('printers.move') }}</AppButton>
      </div>
      <div class="row buttons">
        <AppButton :icon="Home" data-control="home" :disabled="controls.busy.value" @click="controls.send({ action: 'home' })">{{ t('printers.homeAll') }}</AppButton>
        <AppButton variant="danger" :icon="OctagonX" data-control="emergency_stop" :disabled="controls.busy.value" @click="controls.send({ action: 'emergency_stop' })">
          {{ t('printers.emergencyStop') }}
        </AppButton>
      </div>
    </div>
  </Card>
</template>

<style scoped>
.rows {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-2);
}
.row > :deep(.field) {
  flex: 1 1 120px;
}
.buttons {
  justify-content: space-between;
}
</style>
