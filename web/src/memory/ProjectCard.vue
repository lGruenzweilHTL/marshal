<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { formatDateTime } from '../format'
import { aliasStatus } from './memoryLabels'

const props = defineProps<{ project: Schemas['ProjectView'] }>()
const emit = defineEmits<{ changed: [] }>()

const session = useApi()
const edit = reactive({ name: props.project.name, description: props.project.description })
const alias = ref('')
const editing = ref(false)
const feedback = ref<Schemas['ProjectFeedbackView'][] | null>(null)
const error = ref<string | null>(null)
const path = () => ({ params: { path: { id: props.project.id } } })

watch(
  () => props.project,
  (project) => Object.assign(edit, { name: project.name, description: project.description }),
)

async function save() {
  const { data, error: problem } = await session.api.PUT('/api/v1/memory/projects/{id}', { ...path(), body: { name: edit.name, description: edit.description } })
  error.value = data ? null : problemMessage(problem, 'The project could not be saved.')
  if (data) {
    editing.value = false
    emit('changed')
  }
}

async function remove() {
  if (!window.confirm(`Delete the project "${props.project.name}"? Its memory stays and shows up in the audit.`)) return
  const { response } = await session.api.DELETE('/api/v1/memory/projects/{id}', path())
  error.value = response.ok ? null : 'The project could not be deleted.'
  if (response.ok) emit('changed')
}

async function addAlias() {
  const { response, error: problem } = await session.api.POST('/api/v1/memory/projects/{id}/aliases', { ...path(), body: { phrase: alias.value } })
  error.value = response.ok ? null : problemMessage(problem, 'The alias could not be added.')
  if (response.ok) {
    alias.value = ''
    emit('changed')
  }
}

async function setStatus(aliasId: string, status: Schemas['ProjectAliasStatus']) {
  const { response } = await session.api.PUT('/api/v1/memory/projects/{id}/aliases/{aliasId}', {
    params: { path: { id: props.project.id, aliasId } },
    body: { status },
  })
  error.value = response.ok ? null : 'The alias could not be changed.'
  emit('changed')
}

async function removeAlias(aliasId: string) {
  const { response } = await session.api.DELETE('/api/v1/memory/projects/{id}/aliases/{aliasId}', { params: { path: { id: props.project.id, aliasId } } })
  error.value = response.ok ? null : 'The alias could not be removed.'
  emit('changed')
}

async function toggleFeedback() {
  if (feedback.value) {
    feedback.value = null
    return
  }
  const { data } = await session.api.GET('/api/v1/memory/projects/{id}/feedback', path())
  feedback.value = data ?? []
}
</script>

<template>
  <article class="card" :data-project="project.id">
    <h2>{{ project.name }}</h2>
    <p class="muted">
      {{ project.chunkCount }} memory chunk{{ project.chunkCount === 1 ? '' : 's' }}
      <template v-if="project.lastActiveAt"> · last active {{ formatDateTime(project.lastActiveAt) }}</template>
      · <RouterLink :to="{ path: '/memory', query: { project: project.id } }">show memory</RouterLink>
    </p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="project.description">{{ project.description }}</p>
    <div class="actions">
      <button type="button" data-test="edit" @click="editing = !editing">{{ editing ? 'Cancel' : 'Edit' }}</button>
      <button type="button" data-test="delete" @click="remove">Delete</button>
    </div>

    <form v-if="editing" class="stack" data-test="edit-project" @submit.prevent="save">
      <label>Name <input v-model="edit.name" name="name" required maxlength="100" /></label>
      <label>Description <textarea v-model="edit.description" name="description" rows="2" maxlength="2000"></textarea></label>
      <button type="submit">Save</button>
    </form>

    <h3>Aliases</h3>
    <ul class="plain">
      <li v-for="entry in project.aliases" :key="entry.id" :data-alias="entry.phrase">
        “{{ entry.phrase }}” <span class="badge">{{ aliasStatus(entry) }}</span>
        <button v-if="entry.status !== 'active'" type="button" data-test="promote" @click="setStatus(entry.id, 'active')">Accept</button>
        <button v-if="entry.status !== 'rejected'" type="button" data-test="reject" @click="setStatus(entry.id, 'rejected')">Reject</button>
        <button type="button" data-test="remove-alias" @click="removeAlias(entry.id)">Remove</button>
      </li>
    </ul>
    <form class="inline-form" data-test="add-alias" @submit.prevent="addAlias">
      <input v-model="alias" name="alias" placeholder="another name for the project" aria-label="New alias" required />
      <button type="submit">Add alias</button>
    </form>

    <button type="button" data-test="feedback" @click="toggleFeedback">{{ feedback ? 'Hide' : 'Show' }} corrections</button>
    <ul v-if="feedback" class="plain" data-test="feedback-log">
      <li v-if="!feedback.length" class="muted">No corrections yet.</li>
      <li v-for="entry in feedback" :key="entry.id">
        {{ formatDateTime(entry.createdAt) }} · {{ entry.kind }} · “{{ entry.phrase }}”
        <RouterLink v-if="entry.runId" :to="`/runs/${entry.runId}`">run</RouterLink>
      </li>
    </ul>
  </article>
</template>

<style scoped>
.card {
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--border);
  overflow-wrap: anywhere;
}

.actions,
.inline-form {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.inline-form input {
  flex: 1 1 12rem;
  min-width: 0;
}

li + li {
  margin-top: 0.25rem;
}
</style>
