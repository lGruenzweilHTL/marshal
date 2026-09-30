<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'
import { decideApproval, decideProposal } from './decisions'
import DiffView from './DiffView.vue'

const session = useApi()
const route = useRoute()
const approvals = ref<Schemas['ApprovalItem'][]>([])
const proposals = ref<Schemas['ProposalRecord'][]>([])
const diffs = reactive<Record<string, string>>({})
const errors = reactive<Record<string, string | null>>({})
const loadError = ref<string | null>(null)
const busy = ref<string | null>(null)

/** The approval a push notification links to (`/review/<id>`). */
const focusId = computed(() => (typeof route.params.id === 'string' ? route.params.id : null))

async function load() {
  const [approvalList, proposalList] = await Promise.all([session.api.GET('/api/v1/approvals'), session.api.GET('/api/v1/proposals')])
  if (approvalList.data) approvals.value = approvalList.data
  if (proposalList.data) proposals.value = proposalList.data
  loadError.value = approvalList.data && proposalList.data ? null : 'The review queue could not be loaded.'
}

async function decide(approval: Schemas['ApprovalItem'], approve: boolean) {
  busy.value = approval.id
  const failure = await decideApproval(session, approval.id, approve)
  busy.value = null
  errors[approval.id] = failure
  if (!failure) approvals.value = approvals.value.filter((candidate) => candidate.id !== approval.id)
}

async function showDiff(proposal: Schemas['ProposalRecord']) {
  const { data } = await session.api.GET('/api/v1/proposals/{id}', { params: { path: { id: proposal.id } } })
  if (data?.diff != null) diffs[proposal.id] = data.diff
  else errors[proposal.id] = 'The diff could not be loaded.'
}

async function decideChange(proposal: Schemas['ProposalRecord'], merge: boolean) {
  busy.value = proposal.id
  const failure = await decideProposal(session, proposal.id, merge)
  busy.value = null
  errors[proposal.id] = failure
  if (!failure) proposals.value = proposals.value.filter((candidate) => candidate.id !== proposal.id)
}

function argumentsText(value: unknown): string {
  return JSON.stringify(value, null, 2)
}

onMounted(async () => {
  await load()
  if (focusId.value) {
    await nextTick()
    document.querySelector(`[data-approval="${CSS.escape(focusId.value)}"]`)?.scrollIntoView?.({ block: 'center' })
  }
})

// A task that waits for an approval changed; so may the queue.
useLiveEvent('tasks', 'task.changed', () => void load())
</script>

<template>
  <section class="page">
    <h1>Review</h1>
    <p v-if="loadError" role="alert" class="error">{{ loadError }}</p>

    <h2>Approvals</h2>
    <article
      v-for="approval in approvals"
      :key="approval.id"
      :data-approval="approval.id"
      :class="['card', { focused: approval.id === focusId }]"
    >
      <h3>{{ approval.summary }}</h3>
      <p>
        <code>{{ approval.tool }}</code>
        <span class="muted"> · Expires {{ formatDateTime(approval.expiresAt) }}</span>
        <template v-if="approval.taskId"> · <RouterLink :to="`/tasks/${approval.taskId}`">task</RouterLink></template>
      </p>
      <pre class="arguments">{{ argumentsText(approval.arguments) }}</pre>
      <ul class="reasons">
        <li v-for="(reason, index) in approval.reasons" :key="index" data-test="reason">{{ reason.sentence }}</li>
      </ul>
      <p v-if="approval.taint.length" data-test="taint" class="muted">
        Read untrusted content:
        <template v-for="(source, index) in approval.taint" :key="source.kind">
          <template v-if="index > 0">, </template>
          <RouterLink v-if="approval.runId" :to="`/runs/${approval.runId}#step-${source.firstStep}`">{{ source.kind }} (step {{ source.firstStep }})</RouterLink>
          <template v-else>{{ source.kind }} (step {{ source.firstStep }})</template>
        </template>
      </p>
      <div class="actions">
        <button type="button" data-test="approve" :disabled="busy === approval.id" @click="decide(approval, true)">Approve</button>
        <button type="button" data-test="reject" :disabled="busy === approval.id" @click="decide(approval, false)">Reject</button>
      </div>
      <p v-if="errors[approval.id]" role="alert" class="error">{{ errors[approval.id] }}</p>
    </article>
    <p v-if="approvals.length === 0" class="muted">No approvals waiting.</p>

    <h2>Code changes</h2>
    <article v-for="change in proposals" :key="change.id" :data-proposal="change.id" class="card">
      <h3>{{ change.title }}</h3>
      <p>
        <code>{{ change.target }}</code> · branch <code>{{ change.branch }}</code>
        <span class="muted"> · {{ formatDateTime(change.createdAt) }}</span>
        <template v-if="change.taskId"> · <RouterLink :to="`/tasks/${change.taskId}`">task</RouterLink></template>
      </p>
      <DiffView v-if="diffs[change.id] !== undefined" :diff="diffs[change.id]!" />
      <button v-else type="button" data-test="show-diff" @click="showDiff(change)">Show diff</button>
      <div class="actions">
        <button type="button" data-test="merge" :disabled="busy === change.id" @click="decideChange(change, true)">Merge</button>
        <button type="button" data-test="reject" :disabled="busy === change.id" @click="decideChange(change, false)">Reject</button>
      </div>
      <p v-if="errors[change.id]" role="alert" class="error">{{ errors[change.id] }}</p>
    </article>
    <p v-if="proposals.length === 0" class="muted">No code changes waiting.</p>

    <p v-if="approvals.length === 0 && proposals.length === 0 && !loadError">Nothing waits for your review.</p>
  </section>
</template>

<style scoped>
.card {
  padding: 1rem;
  margin-bottom: 1rem;
  border: 1px solid var(--border);
  border-radius: 12px;
  min-width: 0;
}

.card h3 {
  margin: 0 0 0.25rem;
}

.card.focused {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--accent);
}

.arguments {
  overflow-x: auto;
  padding: 0.5rem;
  background: var(--surface);
  border-radius: 8px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.5rem;
}
</style>
