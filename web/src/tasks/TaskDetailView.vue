<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { renderMarkdown } from '../chat/markdown'
import { formatCost, formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'
import { budgetUse, describeTrigger, isUnfinished, taskLabel } from './taskFormat'

const session = useApi()
const route = useRoute()
const detail = ref<Schemas['TaskDetail'] | null>(null)
const error = ref<string | null>(null)
const editing = ref(false)
const goal = ref('')
const confirmingCancel = ref(false)

const id = computed(() => (typeof route.params.id === 'string' ? route.params.id : null))

async function load() {
  if (!id.value) return
  const { data, response } = await session.api.GET('/api/v1/tasks/{id}', { params: { path: { id: id.value } } })
  if (data) detail.value = data
  error.value = data ? null : response.status === 404 ? 'This task does not exist.' : 'The task could not be loaded.'
}

function edit() {
  goal.value = detail.value?.task.goal ?? ''
  editing.value = true
}

async function saveGoal() {
  if (!id.value || !detail.value) return
  const { data, error: problem } = await session.api.PUT('/api/v1/tasks/{id}/goal', { params: { path: { id: id.value } }, body: { goal: goal.value } })
  if (!data) {
    error.value = problemMessage(problem, 'The goal could not be saved.')
    return
  }
  error.value = null
  detail.value = { ...detail.value, task: data }
  editing.value = false
}

async function cancel() {
  if (!id.value) return
  confirmingCancel.value = false
  const { data, error: problem } = await session.api.POST('/api/v1/tasks/{id}/cancel', { params: { path: { id: id.value } } })
  if (!data) {
    error.value = problemMessage(problem, 'The task could not be cancelled.')
    return
  }
  await load()
}

function describeDelivery(delivery: Schemas['TaskDelivery']): string {
  const parts: string[] = [delivery.channel]
  if (delivery.target) parts.push(`to ${delivery.target}`)
  if (delivery.priority && delivery.priority !== 'normal') parts.push(delivery.priority)
  if (delivery.followUp) parts.push('listens for a reply')
  if (delivery.when?.length) parts.push(`when ${delivery.when.join(' and ')}`)
  if (delivery.expires) parts.push(`expires ${delivery.expires}`)
  if (delivery.fallback) parts.push(`then ${delivery.fallback}`)
  if (delivery.pendingSince) parts.push(`waiting since ${formatDateTime(delivery.pendingSince)}`)
  return parts.join(' · ')
}

watch(id, () => {
  detail.value = null
  editing.value = false
  void load()
}, { immediate: true })

useLiveEvent('tasks', 'task.changed', (payload) => {
  if (payload.taskId === id.value && !editing.value) void load()
})
</script>

<template>
  <section class="page">
    <p><RouterLink to="/tasks">← Tasks</RouterLink></p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <template v-if="detail">
      <h1>{{ taskLabel(detail.task) }}</h1>
      <p data-test="status">
        <span :class="['badge', detail.task.status === 'failed' ? 'unhealthy' : 'healthy']">{{ detail.task.status }}</span>
        <span v-if="detail.task.statusReason" class="muted"> {{ detail.task.statusReason }}</span>
        <template v-if="isUnfinished(detail.task.status)">
          <button v-if="!confirmingCancel" type="button" data-test="cancel" @click="confirmingCancel = true">Cancel task</button>
          <span v-else>
            Cancels this task and its unfinished child tasks.
            <button type="button" data-test="cancel-confirm" @click="cancel">Cancel now</button>
            <button type="button" @click="confirmingCancel = false">Keep it</button>
          </span>
        </template>
      </p>

      <h2>Goal</h2>
      <form v-if="editing" data-test="goal-form" class="stack" @submit.prevent="saveGoal">
        <textarea v-model="goal" name="goal" rows="5" aria-label="Goal"></textarea>
        <div>
          <button type="submit">Save goal</button>
          <button type="button" @click="editing = false">Discard</button>
        </div>
      </form>
      <template v-else>
        <p data-test="goal" class="goal">{{ detail.task.goal }}</p>
        <button type="button" data-test="edit-goal" @click="edit">Edit goal</button>
      </template>

      <dl class="facts">
        <dt>Trigger</dt>
        <dd><code>{{ describeTrigger(detail.task.trigger) }}</code></dd>
        <dt>Next run</dt>
        <dd>{{ detail.task.nextRunAt ? formatDateTime(detail.task.nextRunAt) : '–' }}</dd>
        <dt>Cost / budget</dt>
        <dd>{{ budgetUse(detail.task) }} ({{ detail.task.spent.runs }} runs)</dd>
        <dt>Created</dt>
        <dd>{{ formatDateTime(detail.task.createdAt) }} by {{ detail.task.createdBy }}</dd>
        <template v-if="detail.overrideQuietHoursUntil">
          <dt>Quiet hours override</dt>
          <dd>until {{ formatDateTime(detail.overrideQuietHoursUntil) }}</dd>
        </template>
      </dl>

      <template v-if="detail.notes">
        <h2>Notes</h2>
        <p data-test="notes" class="goal">{{ detail.notes }}</p>
      </template>

      <h2>Latest result</h2>
      <template v-if="detail.result">
        <p class="muted" v-if="detail.task.resultAt">{{ formatDateTime(detail.task.resultAt) }}</p>
        <!-- eslint-disable-next-line vue/no-v-html -- renderMarkdown escapes HTML and allows same-origin images only -->
        <div data-test="result" class="markdown" v-html="renderMarkdown(detail.result.body)"></div>
      </template>
      <p v-else class="muted">No result yet.</p>

      <h2>Deliveries</h2>
      <ul v-if="detail.deliveries.length" data-test="deliveries" class="plain">
        <li v-for="(delivery, index) in detail.deliveries" :key="index">{{ describeDelivery(delivery) }}</li>
      </ul>
      <p v-else class="muted">The result stays here.</p>

      <template v-if="detail.executorSessions.length">
        <h2>Executor sessions</h2>
        <ul data-test="executor-sessions" class="plain">
          <li v-for="executor in detail.executorSessions" :key="executor.id">
            {{ executor.executor }}<template v-if="executor.target"> on {{ executor.target }}</template> ·
            <span :class="['badge', executor.status === 'failed' ? 'unhealthy' : 'healthy']">{{ executor.status }}</span> ·
            {{ formatDateTime(executor.startedAt) }}
          </li>
        </ul>
      </template>

      <h2>Runs</h2>
      <ul v-if="detail.runs.length" data-test="runs" class="plain">
        <li v-for="run in detail.runs" :key="run.id">
          <RouterLink :to="`/runs/${run.id}`">{{ formatDateTime(run.startedAt) }}</RouterLink>
          {{ run.outcome }}<template v-if="run.outcomeReason"> ({{ run.outcomeReason }})</template> · {{ formatCost(run.cost) }}
        </li>
      </ul>
      <p v-else class="muted">No runs yet.</p>

      <h2>Notifications</h2>
      <ul v-if="detail.notifications.length" data-test="notifications" class="plain">
        <li v-for="notification in detail.notifications" :key="notification.id">
          {{ formatDateTime(notification.createdAt) }} · {{ notification.outcome }}<template v-if="notification.channel"> via {{ notification.channel }}</template
          ><template v-if="notification.reason"> ({{ notification.reason }})</template>: {{ notification.content }}
        </li>
      </ul>
      <p v-else class="muted">Nothing sent yet.</p>
    </template>
  </section>
</template>

<style scoped>
.goal {
  white-space: pre-wrap;
}

textarea {
  width: 100%;
  font: inherit;
}
</style>
