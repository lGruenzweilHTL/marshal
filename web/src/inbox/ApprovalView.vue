<script setup lang="ts">
import { Activity, Zap } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section } from '../ui'
import ApprovalTicket from './ApprovalTicket.vue'
import { findIn, isNull } from './find'

/** The approval deep link (push action, Home, Inbox): the full ticket and where it came from. */
const api = useApi()
const route = useRoute()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

const approval = useLoad(() => findIn(api.call((client) => client.GET('/api/v1/approvals')), id), { isEmpty: isNull })
</script>

<template>
  <Page width="narrow">
    <PageHeader :title="t('inbox.approval')" back="/inbox" :back-label="t('inbox.title')" />
    <LoadBoundary :load="approval" size="page" :empty-title="t('inbox.approvalNotFound')">
      <template #default="{ data }">
        <template v-if="data">
        <ApprovalTicket :approval="data" />
        <p class="note">{{ t('inbox.ticket.onlyYou') }}</p>
        <Section :title="t('inbox.origin')">
          <ListCard>
            <ListRow v-if="data.taskId" :title="t('inbox.context.task')" :subtitle="data.taskId" :to="`/tasks/${data.taskId}`">
              <template #lead><IconTile :icon="Zap" /></template>
            </ListRow>
            <ListRow v-if="data.runId" :title="t('inbox.context.run')" :subtitle="format.dateTime(data.createdAt)" :to="`/runs/${data.runId}`">
              <template #lead><IconTile :icon="Activity" /></template>
            </ListRow>
          </ListCard>
        </Section>
      </template>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.note {
  padding: 0 var(--space-3);
  text-align: center;
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
</style>
