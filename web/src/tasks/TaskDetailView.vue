<script setup lang="ts">
import { Bell, Check, CircleStop, Pencil, Radio, X } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { renderMarkdown } from '../chat/markdown'
import { useFormat } from '../i18n/locale'
import NotificationRow from '../inbox/NotificationRow.vue'
import { useLiveEvent } from '../live/liveEvents'
import RunRow from '../runs/RunRow.vue'
import {
  AppButton,
  Badge,
  Card,
  Dialog,
  ErrorBanner,
  Field,
  IconTile,
  KeyValueList,
  ListCard,
  ListRow,
  LoadBoundary,
  LoadState,
  Page,
  PageHeader,
  Section,
  useDesktop,
} from '../ui'
import BudgetMeter from './BudgetMeter.vue'
import TaskToolPolicy from './TaskToolPolicy.vue'
import TriggerLabel from './TriggerLabel.vue'
import { isUnfinished, spanLabel, taskLabel } from './taskFormat'
import { statusTone } from './taskTone'

/**
 * One task: goal (editable), preparation run, latest result, runs, deliveries and notifications, with its trigger and budget use.
 * Push actions for a task result link here.
 */
const api = useApi()
const route = useRoute()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

const detail = useLoad(() => api.call((client) => client.GET('/api/v1/tasks/{id}', { params: { path: { id } } })), { isEmpty: () => false })
// Answers of edits and cancels are shown until the next load replaces them.
const task = shallowRef<Schemas['TaskItem'] | null>(null)
const current = computed(() => task.value ?? detail.data.value?.task ?? null)

const editing = shallowRef(false)
const goal = shallowRef('')
const saveGoal = useSubmit(async () => {
  const result = await api.call((client) => client.PUT('/api/v1/tasks/{id}/goal', { params: { path: { id } }, body: { goal: goal.value } }))
  if (result.data) {
    task.value = result.data
    editing.value = false
  }
  return result
})

const asking = shallowRef(false)
const cancel = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/tasks/{id}/cancel', { params: { path: { id } } }))
  if (result.data) {
    asking.value = false
    task.value = null
    detail.refresh()
  }
  return result
})

function edit() {
  goal.value = current.value?.goal ?? ''
  editing.value = true
}

useLiveEvent('tasks', 'task.changed', (payload) => {
  if (payload.taskId === id && !editing.value) {
    task.value = null
    detail.refresh()
  }
})

function describeDelivery(delivery: Schemas['TaskDelivery']): string {
  const parts: string[] = []
  if (delivery.target) parts.push(t('tasks.detail.delivery.to', { target: delivery.target }))
  if (delivery.priority && delivery.priority !== 'normal') parts.push(t(`inbox.notification.priority.${delivery.priority}`))
  if (delivery.followUp) parts.push(t('tasks.detail.delivery.followUp'))
  if (delivery.when?.length) parts.push(t('tasks.detail.delivery.when', { condition: delivery.when.join(' & ') }))
  if (delivery.expires) parts.push(t('tasks.detail.delivery.expires', { time: delivery.expires }))
  if (delivery.fallback) parts.push(t('tasks.detail.delivery.then', { channel: delivery.fallback }))
  if (delivery.pendingSince) parts.push(t('tasks.detail.delivery.waiting', { time: format.value.dateTime(delivery.pendingSince) }))
  return parts.join(' · ')
}
</script>

<template>
  <Page width="wide">
    <PageHeader v-if="!current" :title="t('tasks.title')" back="/tasks" :back-label="t('tasks.title')" />
    <LoadBoundary :load="detail" size="page">
      <template v-if="detail.data.value && current">
        <PageHeader :title="taskLabel(current)" back="/tasks" :back-label="t('tasks.title')">
          <template v-if="isUnfinished(current.status)" #actions>
            <AppButton :icon="CircleStop" data-test="cancel" @click="asking = true">{{ t('tasks.detail.cancel') }}</AppButton>
          </template>
        </PageHeader>
        <p class="status" data-test="status">
          <Badge :tone="statusTone[current.status]" dot :label="t(`tasks.status.${current.status}`)" />
          <span v-if="current.statusReason" class="ink-3">{{ current.statusReason }}</span>
        </p>

        <div class="layout" :class="{ split: desktop }">
          <div class="main">
            <Card>
              <span class="t-overline">{{ t('tasks.detail.goal') }}</span>
              <form v-if="editing" data-test="goal-form" class="goal-form" @submit.prevent="saveGoal.submit()">
                <Field v-model="goal" kind="textarea" name="goal" :rows="4" :label="t('tasks.detail.goal')" hide-label />
                <ErrorBanner v-if="saveGoal.error.value" :error="saveGoal.error.value" />
                <div class="buttons">
                  <AppButton type="submit" variant="primary" size="sm" :icon="Check" :loading="saveGoal.busy.value">{{ t('tasks.detail.saveGoal') }}</AppButton>
                  <AppButton size="sm" variant="ghost" :icon="X" @click="editing = false">{{ t('tasks.detail.discard') }}</AppButton>
                </div>
              </form>
              <template v-else>
                <p data-test="goal" class="goal">{{ current.goal }}</p>
                <AppButton size="sm" :icon="Pencil" data-test="edit-goal" @click="edit">{{ t('tasks.detail.editGoal') }}</AppButton>
              </template>
            </Card>

            <Card v-if="detail.data.value.notes">
              <span class="t-overline">{{ t('tasks.detail.notes') }}</span>
              <p data-test="notes" class="goal">{{ detail.data.value.notes }}</p>
            </Card>

            <Card v-if="detail.data.value.preparation" data-test="preparation">
              <span class="t-overline">{{ t('tasks.detail.preparation') }}</span>
              <p class="goal">{{ detail.data.value.preparation.goal }}</p>
              <p class="ink-3 prep-facts">
                {{ t('tasks.detail.prepareBefore', { span: spanLabel(detail.data.value.preparation.before) }) }}
                <template v-if="detail.data.value.preparation.nextAt">
                  · <span class="num">{{ t('tasks.detail.prepareNext', { time: format.dateTime(detail.data.value.preparation.nextAt) }) }}</span>
                </template>
              </p>
              <p v-if="detail.data.value.preparation.preparedAt" class="prep-facts num">{{ t('tasks.detail.preparedAt', { time: format.dateTime(detail.data.value.preparation.preparedAt) }) }}</p>
            </Card>

            <Card>
              <span class="t-overline">{{ current.resultAt ? t('tasks.detail.resultAt', { time: format.dateTime(current.resultAt) }) : t('tasks.detail.result') }}</span>
              <!-- eslint-disable-next-line vue/no-v-html -- renderMarkdown escapes HTML and allows same-origin images only -->
              <div v-if="detail.data.value.result" data-test="result" class="markdown" v-html="renderMarkdown(detail.data.value.result.body)"></div>
              <p v-else class="ink-3">{{ t('tasks.detail.noResult') }}</p>
            </Card>

            <TaskToolPolicy :task-id="id" :policy="detail.data.value.toolPolicy" :available="detail.data.value.availableTools" @saved="detail.refresh()" />

            <Section :title="t('tasks.detail.runs')" :count="detail.data.value.runs.length" data-test="runs">
              <ListCard v-if="detail.data.value.runs.length"><RunRow v-for="run in detail.data.value.runs" :key="run.id" :run="run" /></ListCard>
              <LoadState v-else state="empty" :title="t('tasks.detail.noRuns')" />
            </Section>

            <Section :title="t('tasks.detail.deliveries')" data-test="deliveries">
              <ListCard v-if="detail.data.value.deliveries.length">
                <ListRow
                  v-for="(delivery, index) in detail.data.value.deliveries"
                  :key="index"
                  :title="delivery.channel"
                  :subtitle="describeDelivery(delivery)"
                  :chevron="false"
                >
                  <template #lead><IconTile :icon="delivery.channel === 'voice' ? Radio : Bell" tone="info" /></template>
                </ListRow>
              </ListCard>
              <LoadState v-else state="empty" :title="t('tasks.detail.noDeliveries')" />
            </Section>

            <Section v-if="detail.data.value.executorSessions.length" :title="t('tasks.detail.executors')" data-test="executor-sessions">
              <ListCard>
                <ListRow
                  v-for="executor in detail.data.value.executorSessions"
                  :key="executor.id"
                  :title="[executor.executor, executor.target].filter(Boolean).join(' · ')"
                  :subtitle="format.dateTime(executor.startedAt)"
                  :chevron="false"
                >
                  <template #trail><Badge :tone="executor.status === 'failed' ? 'danger' : 'info'" :label="executor.status" /></template>
                </ListRow>
              </ListCard>
            </Section>

            <Section :title="t('tasks.detail.notifications')" data-test="notifications">
              <ListCard v-if="detail.data.value.notifications.length">
                <NotificationRow v-for="item in detail.data.value.notifications" :key="item.id" :notification="item" />
              </ListCard>
              <LoadState v-else state="empty" :title="t('tasks.detail.noNotifications')" />
            </Section>
          </div>

          <Card :title="t('tasks.detail.details')" class="facts" data-test="facts">
            <KeyValueList
              :items="[
                { key: 'trigger', label: t('tasks.detail.trigger') },
                { key: 'next', label: t('tasks.detail.next'), value: current.nextRunAt ? format.dateTime(current.nextRunAt) : null },
                { key: 'created', label: t('tasks.detail.created'), value: t('tasks.detail.createdBy', { time: format.dateTime(current.createdAt), who: current.createdBy }) },
                ...(detail.data.value.overrideQuietHoursUntil
                  ? [{ key: 'quiet', label: t('tasks.detail.quietOverride'), value: t('tasks.detail.quietUntil', { time: format.dateTime(detail.data.value.overrideQuietHoursUntil) }) }]
                  : []),
              ]"
            >
              <template #trigger><TriggerLabel :trigger="current.trigger" /></template>
            </KeyValueList>
            <div class="meters">
              <BudgetMeter :label="t('tasks.detail.cost')" :spent="current.spent.cost" :budget="current.budget.cost" />
              <BudgetMeter :label="t('tasks.detail.runsMeter')" kind="count" :spent="current.spent.runs" :budget="current.budget.runs" />
              <BudgetMeter :label="t('tasks.detail.steps')" kind="count" :spent="current.spent.steps" :budget="current.budget.steps" />
            </div>
          </Card>
        </div>

        <Dialog :open="asking" :title="t('tasks.detail.cancelTitle')" :description="t('tasks.detail.cancelBody')" @close="asking = false">
          <ErrorBanner v-if="cancel.error.value" :error="cancel.error.value" />
          <template #actions>
            <AppButton @click="asking = false">{{ t('tasks.detail.keep') }}</AppButton>
            <AppButton variant="danger" :icon="CircleStop" data-test="cancel-confirm" :loading="cancel.busy.value" @click="cancel.submit()">
              {{ t('tasks.detail.cancelConfirm') }}
            </AppButton>
          </template>
        </Dialog>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.status {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: calc(-1 * var(--space-4));
  font: var(--text-body-s);
}
.layout,
.main {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: 28px;
  align-items: start;
}
.split .facts {
  position: sticky;
  top: var(--space-8);
}
.layout:not(.split) .facts {
  order: -1;
}
.goal {
  margin: var(--space-2) 0 var(--space-3);
  font: 400 17px/26px var(--font-sans);
  white-space: pre-wrap;
}
.prep-facts {
  font: var(--text-body-s);
}
.goal-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin-top: var(--space-2);
}
.buttons {
  display: flex;
  gap: var(--space-2);
}
.markdown {
  margin-top: var(--space-2);
}
.markdown :deep(h2) {
  font: var(--text-title-3);
  margin: var(--space-3) 0 var(--space-1);
}
.markdown :deep(p) {
  margin: 0 0 var(--space-2);
}
.meters {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin-top: var(--space-4);
  padding-top: var(--space-4);
  border-top: 1px solid var(--line);
}
</style>
