<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { problemMessage } from '../api/problem'
import { formatDateTime } from '../format'
import { useUserSession } from '../session'

type Secret = Schemas['SecretItem']

const session = useApiSession()
const user = useUserSession()
const overview = ref<Schemas['SecretOverview'] | null>(null)
const error = ref<string | null>(null)
const saved = ref<string | null>(null)
const name = ref('')
const value = ref('')
const scope = ref<'user' | 'instance'>('user')
const nameInput = ref<HTMLInputElement | null>(null)
const isAdmin = computed(() => user.current.value?.roles.includes('admin') === true)

async function load() {
  const { data } = await session.raw.GET('/api/v1/me/secrets')
  if (data) overview.value = data
  else error.value = 'The secrets could not be loaded.'
}

function prefill(secretName: string) {
  name.value = secretName
  nameInput.value?.focus()
}

async function save() {
  error.value = null
  saved.value = null
  const body = { value: value.value }
  const { response, error: problem } =
    scope.value === 'instance'
      ? await session.raw.PUT('/api/v1/secrets/instance/{name}', { params: { path: { name: name.value } }, body })
      : await session.raw.PUT('/api/v1/me/secrets/{name}', { params: { path: { name: name.value } }, body })
  value.value = ''
  if (!response.ok) {
    error.value = problemMessage(problem, 'The secret could not be stored.')
    return
  }
  saved.value = `Stored ${name.value}.`
  name.value = ''
  await load()
}

async function remove(secret: Secret) {
  if (!window.confirm(`Delete the secret "${secret.name}"? Integrations using it stop working.`)) return
  const path = { params: { path: { name: secret.name } } }
  const { response } =
    secret.scope === 'instance' ? await session.raw.DELETE('/api/v1/secrets/instance/{name}', path) : await session.raw.DELETE('/api/v1/me/secrets/{name}', path)
  if (!response.ok) error.value = 'The secret could not be deleted.'
  await load()
}

onMounted(load)
</script>

<template>
  <section class="page">
    <h1>Secrets</h1>
    <p class="muted">Values are write-only: Marshal uses them but never shows them again.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="saved" role="status">{{ saved }}</p>

    <template v-if="overview">
      <div v-if="overview.unresolved.length" class="unresolved">
        <h2>Missing secrets</h2>
        <ul class="plain">
          <li v-for="missing in overview.unresolved" :key="missing.name" :data-unresolved="missing.name">
            <strong>{{ missing.name }}</strong> <span class="badge unhealthy">unresolved</span>
            <div class="muted">referenced by {{ missing.referencedBy.join('; ') }}</div>
            <button type="button" data-test="set" @click="prefill(missing.name)">Set value</button>
          </li>
        </ul>
      </div>

      <div class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Scope</th>
              <th>Source</th>
              <th>Updated</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="secret in overview.secrets" :key="`${secret.scope}/${secret.name}`" :data-secret="`${secret.scope}/${secret.name}`">
              <td>{{ secret.name }}</td>
              <td>{{ secret.scope }}</td>
              <td>{{ secret.origin }}</td>
              <td>{{ secret.updatedAt ? formatDateTime(secret.updatedAt) : '–' }}</td>
              <td>
                <button v-if="!secret.readOnly" type="button" data-test="delete" @click="remove(secret)">Delete</button>
                <span v-else class="muted">read-only</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <form class="stack" data-test="set-secret" autocomplete="off" @submit.prevent="save">
      <h2>Set a secret</h2>
      <label>Name <input ref="nameInput" v-model="name" name="secretName" required pattern="[a-z][a-z0-9_]{0,63}" title="lowercase letter first, then lowercase letters, digits and _" /></label>
      <label>Value <input v-model="value" name="secretValue" type="password" autocomplete="new-password" required /></label>
      <label v-if="isAdmin"
        >Scope
        <select v-model="scope" name="secretScope">
          <option value="user">Only me</option>
          <option value="instance">Instance (all users, configuration)</option>
        </select>
      </label>
      <button type="submit">Store</button>
    </form>
  </section>
</template>

<style scoped>
.unresolved {
  padding: 0.75rem;
  margin-bottom: 1rem;
  border-left: 4px solid var(--error);
  background: var(--surface);
}

.unresolved h2 {
  margin-top: 0;
  font-size: 1rem;
}

.unresolved li + li {
  margin-top: 0.75rem;
}
</style>
