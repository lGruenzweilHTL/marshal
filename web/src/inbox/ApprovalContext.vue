<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { Card, KeyValueList } from '../ui'

/** Where an approval came from: its task and run (links), when it was asked and when it expires. */
defineProps<{ approval: Schemas['ApprovalItem'] }>()
const { t } = useI18n()
const format = useFormat()
</script>

<template>
  <Card :title="t('inbox.context.title')">
    <KeyValueList
      :items="[
        { key: 'task', label: t('inbox.context.task') },
        { key: 'run', label: t('inbox.context.run') },
        { key: 'created', label: t('inbox.context.created'), value: format.dateTime(approval.createdAt) },
        { key: 'expires', label: t('inbox.context.expires'), value: format.time(approval.expiresAt) },
      ]"
    >
      <template #task>
        <RouterLink v-if="approval.taskId" :to="`/tasks/${approval.taskId}`">{{ approval.taskId }}</RouterLink>
        <template v-else>—</template>
      </template>
      <template #run>
        <RouterLink v-if="approval.runId" :to="`/runs/${approval.runId}`">{{ approval.runId }}</RouterLink>
        <template v-else>—</template>
      </template>
    </KeyValueList>
  </Card>
</template>
