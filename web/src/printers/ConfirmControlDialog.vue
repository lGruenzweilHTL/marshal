<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { AppButton, Dialog, ErrorBanner } from '../ui'
import type { PrinterControls } from './usePrinterControls'

/** Asks before a confirm-tier printer control runs; the server froze the control behind the token. */
defineProps<{ controls: PrinterControls }>()
const { t } = useI18n()
</script>

<template>
  <Dialog :open="!!controls.pending.value" :title="t('printers.confirmTitle')" :description="controls.pending.value?.description" @close="controls.dismiss()">
    <template #actions>
      <AppButton data-test="dismiss" @click="controls.dismiss()">{{ t('actions.cancel') }}</AppButton>
      <AppButton variant="danger" data-test="confirm" :loading="controls.confirming.value" @click="controls.confirm()">{{ t('printers.confirm') }}</AppButton>
    </template>
  </Dialog>
  <ErrorBanner v-if="controls.confirmError.value" :error="controls.confirmError.value" />
</template>
