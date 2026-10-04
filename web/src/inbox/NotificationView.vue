<script setup lang="ts">
import { Inbox, Radio, Zap } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { Card, IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section } from '../ui'
import { findIn, isNull } from './find'
import { priorityLook } from './priority'

/** The notification deep link: the full text, how it was delivered and the task it came from. */
const api = useApi()
const route = useRoute()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

// The API lists notifications only; a deep link looks for the id among the newest ones.
const notification = useLoad(
  () => findIn(api.call((client) => client.GET('/api/v1/notifications', { params: { query: { limit: 200 } } })), id),
  { isEmpty: isNull },
)
</script>

<template>
  <Page width="narrow">
    <PageHeader :title="t('inbox.notification.title')" back="/inbox" :back-label="t('inbox.title')" />
    <LoadBoundary :load="notification" size="page" :empty-title="t('inbox.notification.notFound')">
      <template #default="{ data }">
        <template v-if="data">
        <Card>
          <div class="head">
            <IconTile :icon="priorityLook[data.priority].icon" :tone="priorityLook[data.priority].tone" />
            <div class="who">
              <span class="source">{{ data.source }}</span>
              <span class="when">{{ format.dateTime(data.createdAt) }} · {{ t('inbox.notification.priorityLabel', { priority: t(`inbox.notification.priority.${data.priority}`) }) }}</span>
            </div>
          </div>
          <p class="content">{{ data.content }}</p>
        </Card>
        <Section :title="t('inbox.notification.delivery')">
          <ListCard>
            <ListRow
              :title="t(`inbox.notification.outcome.${data.outcome}`)"
              :subtitle="[data.channel, data.deliveredAt && format.time(data.deliveredAt), data.reason].filter(Boolean).join(' · ')"
            >
              <template #lead><IconTile :icon="data.channel ? Radio : Inbox" :tone="data.outcome === 'delivered' ? 'success' : 'neutral'" /></template>
            </ListRow>
          </ListCard>
        </Section>
        <Section v-if="data.taskId" :title="t('inbox.notification.source')">
          <ListCard>
            <ListRow :title="t('inbox.notification.openTask')" :subtitle="data.taskId" :to="`/tasks/${data.taskId}`">
              <template #lead><IconTile :icon="Zap" /></template>
            </ListRow>
          </ListCard>
        </Section>
      </template>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.who {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.source {
  font: 600 14px/20px var(--font-mono);
}
.when {
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
.content {
  margin-top: var(--space-3);
  font: 400 17px/26px var(--font-sans);
  text-wrap: pretty;
}
</style>
