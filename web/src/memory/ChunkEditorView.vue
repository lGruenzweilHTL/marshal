<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { problemMessage } from '../api/problem'
import { formatDateTime } from '../format'
import { chunkTypes, historyLabels } from './memoryLabels'

type Draft = { scope: Schemas['ChunkScope']; projectId: string; type: Schemas['ChunkType']; title: string; content: string; importance: number; sourceRef: string }

const session = useApiSession()
const route = useRoute()
const router = useRouter()
const detail = ref<Schemas['MemoryChunkDetail'] | null>(null)
const history = ref<Schemas['MemoryHistoryEntry'][]>([])
const projects = ref<Schemas['ProjectView'][]>([])
const error = ref<string | null>(null)
const saved = ref<string | null>(null)
const form = reactive<Draft>(emptyDraft())
const replacement = reactive<Draft>(emptyDraft())
const superseding = ref(false)
const id = computed(() => (typeof route.params.id === 'string' ? route.params.id : null))
const deleted = computed(() => detail.value?.chunk.deletedAt != null)

function emptyDraft(): Draft {
  return { scope: 'general', projectId: '', type: 'fact', title: '', content: '', importance: 3, sourceRef: '' }
}

function body(draft: Draft): Schemas['ChunkWriteRequest'] {
  return {
    scope: draft.scope,
    projectId: draft.scope === 'project' ? draft.projectId : null,
    type: draft.type,
    title: draft.title,
    content: draft.content,
    importance: draft.importance,
    sourceRef: draft.sourceRef.trim() || null,
  }
}

async function load() {
  saved.value = null
  if (!id.value) {
    detail.value = null
    Object.assign(form, emptyDraft())
    return
  }

  const path = { params: { path: { id: id.value } } }
  const [{ data, response }, past] = await Promise.all([session.raw.GET('/api/v1/memory/chunks/{id}', path), session.raw.GET('/api/v1/memory/chunks/{id}/history', path)])
  detail.value = data ?? null
  history.value = past.data ?? []
  error.value = data ? null : response.status === 404 ? 'Memory chunk not found.' : 'The memory chunk could not be loaded.'
  if (data) {
    const chunk = data.chunk
    Object.assign(form, { scope: chunk.scope, projectId: chunk.projectId ?? '', type: chunk.type, title: chunk.title, content: chunk.content, importance: chunk.importance, sourceRef: chunk.sourceRef ?? '' })
    Object.assign(replacement, form)
  }
}

async function save() {
  error.value = null
  saved.value = null
  if (!id.value) {
    const { data, error: problem } = await session.raw.POST('/api/v1/memory/chunks', { body: body(form) })
    if (data) await router.push(`/memory/chunks/${data.id}`)
    else error.value = problemMessage(problem, 'The memory chunk could not be saved.')
    return
  }

  const { data, error: problem } = await session.raw.PUT('/api/v1/memory/chunks/{id}', {
    params: { path: { id: id.value } },
    body: { content: form.content, importance: form.importance, sourceRef: form.sourceRef.trim() || null },
  })
  if (!data) {
    error.value = problemMessage(problem, 'The memory chunk could not be saved.')
    return
  }
  await load()
  saved.value = data.outcome === 'unchanged' ? 'Nothing changed.' : 'Saved.'
}

async function setPinned(pinned: boolean) {
  const path = { params: { path: { id: id.value! } } }
  const { response } = pinned ? await session.raw.PUT('/api/v1/memory/chunks/{id}/pin', path) : await session.raw.DELETE('/api/v1/memory/chunks/{id}/pin', path)
  if (!response.ok) error.value = 'The pin could not be changed.'
  await load()
}

async function remove() {
  if (!window.confirm(`Delete "${detail.value?.chunk.title}"? It will no longer be used in conversations.`)) return
  const { response } = await session.raw.DELETE('/api/v1/memory/chunks/{id}', { params: { path: { id: id.value! } } })
  if (response.ok) await router.push('/memory')
  else error.value = 'The memory chunk could not be deleted.'
}

async function supersede() {
  const { data, error: problem } = await session.raw.POST('/api/v1/memory/chunks/{id}/supersede', { params: { path: { id: id.value! } }, body: body(replacement) })
  if (!data) {
    error.value = problemMessage(problem, 'The replacement could not be saved.')
    return
  }
  superseding.value = false
  await router.push(`/memory/chunks/${data.id}`)
}

watch(id, load)
onMounted(async () => {
  const { data } = await session.raw.GET('/api/v1/memory/projects')
  projects.value = data ?? []
  await load()
})
</script>

<template>
  <section class="page">
    <p><RouterLink to="/memory">← Memory</RouterLink></p>
    <h1>{{ id ? (detail?.chunk.title ?? 'Memory chunk') : 'New memory' }}</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="saved" role="status">{{ saved }}</p>

    <template v-if="!id || detail">
      <p v-if="detail" class="muted">
        {{ detail.chunk.type }} · written by {{ detail.chunk.createdBy }} · created {{ formatDateTime(detail.chunk.createdAt) }}
        <template v-if="detail.chunk.lastRetrievedAt"> · last used {{ formatDateTime(detail.chunk.lastRetrievedAt) }}</template>
        <span v-if="detail.chunk.pinned" class="badge healthy">pinned</span>
        <span v-if="deleted" class="badge unhealthy">deleted</span>
      </p>

      <form class="stack" data-test="chunk" @submit.prevent="save">
        <label
          >Scope
          <select v-model="form.scope" name="scope" :disabled="!!id">
            <option value="general">General</option>
            <option value="project">Project</option>
          </select>
        </label>
        <label v-if="form.scope === 'project'"
          >Project
          <select v-model="form.projectId" name="projectId" :disabled="!!id" required>
            <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
          </select>
        </label>
        <label
          >Type
          <select v-model="form.type" name="type" :disabled="!!id">
            <option v-for="type in chunkTypes" :key="type" :value="type">{{ type }}</option>
          </select>
        </label>
        <label>Title (English) <input v-model="form.title" name="title" :disabled="!!id" required maxlength="200" /></label>
        <label>Content (English) <textarea v-model="form.content" name="content" rows="5" required></textarea></label>
        <label>Importance (1–5) <input v-model.number="form.importance" name="importance" type="number" min="1" max="5" required /></label>
        <label>Source <input v-model="form.sourceRef" name="sourceRef" placeholder="e.g. notes/voron.md" /></label>
        <div class="actions">
          <button type="submit">{{ id ? 'Save' : 'Create' }}</button>
          <template v-if="detail && !deleted">
            <button v-if="detail.chunk.pinned" type="button" data-test="unpin" @click="setPinned(false)">Unpin</button>
            <button v-else type="button" data-test="pin" @click="setPinned(true)">Pin</button>
            <button type="button" data-test="show-supersede" @click="superseding = !superseding">Supersede…</button>
            <button type="button" data-test="delete" @click="remove">Delete</button>
          </template>
        </div>
        <p v-if="id" class="muted">To change the title, type, scope or project, supersede the chunk with a replacement.</p>
      </form>

      <form v-if="superseding" class="stack" data-test="supersede" @submit.prevent="supersede">
        <h2>Replacement</h2>
        <label
          >Scope
          <select v-model="replacement.scope" name="newScope">
            <option value="general">General</option>
            <option value="project">Project</option>
          </select>
        </label>
        <label v-if="replacement.scope === 'project'"
          >Project
          <select v-model="replacement.projectId" name="newProjectId" required>
            <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
          </select>
        </label>
        <label
          >Type
          <select v-model="replacement.type" name="newType">
            <option v-for="type in chunkTypes" :key="type" :value="type">{{ type }}</option>
          </select>
        </label>
        <label>Title <input v-model="replacement.title" name="newTitle" required maxlength="200" /></label>
        <label>Content <textarea v-model="replacement.content" name="newContent" rows="5" required></textarea></label>
        <label>Importance <input v-model.number="replacement.importance" name="newImportance" type="number" min="1" max="5" required /></label>
        <button type="submit">Replace</button>
      </form>

      <template v-if="detail">
        <h2>History</h2>
        <ol class="plain history">
          <li v-for="entry in history" :key="`${entry.kind}/${entry.chunkId}/${entry.validFrom}`" :data-history="entry.kind">
            <strong>{{ historyLabels[entry.kind] }}</strong> · {{ formatDateTime(entry.validFrom) }}
            <RouterLink v-if="entry.chunkId !== id" :to="`/memory/chunks/${entry.chunkId}`">{{ entry.title }}</RouterLink>
            <div>{{ entry.content }}</div>
          </li>
        </ol>
        <template v-if="detail.edges.length">
          <h2>Links</h2>
          <ul class="plain">
            <li v-for="edge in detail.edges" :key="`${edge.from}/${edge.kind}/${edge.to}`" data-test="edge">
              <template v-if="edge.from === id"
                >{{ edge.kind }} → <RouterLink :to="`/memory/chunks/${edge.to}`">{{ edge.to }}</RouterLink></template
              >
              <template v-else
                ><RouterLink :to="`/memory/chunks/${edge.from}`">{{ edge.from }}</RouterLink> {{ edge.kind }} this</template
              >
            </li>
          </ul>
        </template>
      </template>
    </template>
  </section>
</template>

<style scoped>
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.history li {
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  overflow-wrap: anywhere;
}
</style>
