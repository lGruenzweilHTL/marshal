<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { TabNav } from '../ui'

/** The memory area's sections; each memory view shows them under its header. */
const { t } = useI18n()
const tabs = computed(() =>
  (
    [
      ['/memory', 'chunks'],
      ['/memory/projects', 'projects'],
      ['/memory/map', 'map'],
      ['/memory/audit', 'audit'],
    ] as const
  ).map(([to, key]) => ({ to, label: t(`memory.tabs.${key}`) })),
)
</script>

<template>
  <TabNav :tabs="tabs" :label="t('memory.title')" />
</template>
