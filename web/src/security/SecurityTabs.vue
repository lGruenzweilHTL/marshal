<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { TabNav } from '../ui'

/** The security area's sections; `missing` counts secrets that are referenced but have no value. */
const props = defineProps<{ missing?: number }>()
const { t } = useI18n()
const tabs = computed(() => [
  { to: '/security', label: t('security.tabs.access') },
  { to: '/security/secrets', label: t('security.tabs.secrets'), count: props.missing },
])
</script>

<template>
  <TabNav :tabs="tabs" :label="t('security.title')" />
</template>
