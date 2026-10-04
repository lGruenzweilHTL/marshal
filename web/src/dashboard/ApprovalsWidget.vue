<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { useLiveEvent } from '../live/liveEvents'
import { decideApproval } from '../review/decisions'

const session = useApiSession()
const approvals = ref<Schemas['ApprovalItem'][]>([])
const error = ref<string | null>(null)

async function load() {
  const { data } = await session.raw.GET('/api/v1/approvals')
  if (data) approvals.value = data
}

async function decide(approval: Schemas['ApprovalItem'], approve: boolean) {
  error.value = await decideApproval(session, approval.id, approve)
  if (!error.value) approvals.value = approvals.value.filter((candidate) => candidate.id !== approval.id)
}

onMounted(load)
useLiveEvent('tasks', 'task.changed', () => void load())
</script>

<template>
  <h2><RouterLink to="/review">Pending approvals</RouterLink></h2>
  <ul class="plain">
    <li v-for="approval in approvals" :key="approval.id">
      <RouterLink :to="`/review/${approval.id}`">{{ approval.summary }}</RouterLink>
      <div class="actions">
        <button type="button" data-test="approve" @click="decide(approval, true)">Approve</button>
        <button type="button" data-test="reject" @click="decide(approval, false)">Reject</button>
      </div>
    </li>
  </ul>
  <p v-if="approvals.length === 0" class="muted">Nothing to approve.</p>
  <p v-if="error" role="alert" class="error">{{ error }}</p>
</template>

<style scoped>
.actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.25rem;
}
</style>
