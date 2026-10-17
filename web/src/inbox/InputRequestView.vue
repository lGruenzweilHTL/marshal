<script setup lang="ts">
import { Activity, MessageSquare, Zap } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section } from '../ui'
import InputRequestForm from './InputRequestForm.vue'

/** The input request deep link (push, Inbox): the form or page, and where the request came from. */
const api = useApi()
const route = useRoute()
const { t } = useI18n()
const id = String(route.params.id)

const request = useLoad(() => api.call((client) => client.GET('/api/v1/approvals/inputs/{id}', { params: { path: { id } } })), { isEmpty: () => false })
</script>

<template>
  <Page width="narrow">
    <PageHeader :title="t('inbox.input.title')" back="/inbox" :back-label="t('inbox.title')" />
    <LoadBoundary :load="request" size="page" :error-title="request.error.value?.kind === 'problem' && request.error.value.status === 404 ? t('inbox.input.notFound') : undefined">
      <template #default="{ data }">
        <InputRequestForm :request="data" />
        <Section v-if="data.taskId || data.sessionId" :title="t('inbox.origin')">
          <ListCard>
            <ListRow v-if="data.sessionId" :title="t('shell.nav.chat')" :subtitle="data.sessionId" :to="`/chat/${data.sessionId}`">
              <template #lead><IconTile :icon="MessageSquare" /></template>
            </ListRow>
            <ListRow v-if="data.taskId" :title="t('inbox.context.task')" :subtitle="data.taskId" :to="`/tasks/${data.taskId}`">
              <template #lead><IconTile :icon="Zap" /></template>
            </ListRow>
          </ListCard>
        </Section>
        <Section v-else :title="t('inbox.origin')">
          <ListCard>
            <ListRow :title="data.tool" :subtitle="data.serverId">
              <template #lead><IconTile :icon="Activity" /></template>
            </ListRow>
          </ListCard>
        </Section>
      </template>
    </LoadBoundary>
  </Page>
</template>
