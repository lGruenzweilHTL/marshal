<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { problemMessage } from '../api/problem'
import { usePasskeys } from '../auth/passkeys'
import { formatDateTime } from '../format'
import { useUserSession } from '../session'

type Token = Schemas['ApiTokenResponse']

const session = useApiSession()
const user = useUserSession()
const passkeys = usePasskeys()
const tokens = ref<Token[]>([])
const error = ref<string | null>(null)
const tokenName = ref('')
const scopes = ref<string[]>([])
const expiresOn = ref('')
const created = ref<string | null>(null)
const localLogin = ref(false)
const passkeyName = ref('')
const passkeyAdded = ref(false)
const passkeyError = ref<string | null>(null)

async function loadTokens() {
  const { data } = await session.raw.GET('/api/v1/me/tokens')
  if (data) tokens.value = data
  else error.value = 'The tokens could not be loaded.'
}

/** Expiry at the end of the chosen local day. */
function expiry(): string | null {
  if (!expiresOn.value) return null
  const [year, month, day] = expiresOn.value.split('-').map(Number) as [number, number, number]
  return new Date(year, month - 1, day, 23, 59, 59).toISOString()
}

async function createToken() {
  error.value = null
  const { data, error: problem } = await session.raw.POST('/api/v1/me/tokens', {
    body: { name: tokenName.value, scopes: scopes.value, expiresAt: expiry(), kind: null },
  })
  if (!data) {
    error.value = problemMessage(problem, 'The token could not be created.')
    return
  }
  created.value = data.token
  tokenName.value = ''
  scopes.value = []
  expiresOn.value = ''
  await loadTokens()
}

async function revoke(token: Token) {
  if (!window.confirm(`Revoke the token "${token.name}"? Clients using it stop working immediately.`)) return
  const { response } = await session.raw.DELETE('/api/v1/me/tokens/{id}', { params: { path: { id: token.id } } })
  if (!response.ok) error.value = 'The token could not be revoked.'
  await loadTokens()
}

async function copyToken() {
  if (created.value) await navigator.clipboard?.writeText(created.value)
}

async function addPasskey() {
  passkeyAdded.value = false
  passkeyError.value = null
  try {
    const options = await session.raw.POST('/api/v1/auth/local/passkeys/creation-options', { parseAs: 'text' })
    if (!options.data) throw new Error('options')
    const credentialJson = await passkeys.create(options.data)
    const { response, error: problem } = await session.raw.POST('/api/v1/auth/local/passkeys', {
      body: { credentialJson, name: passkeyName.value || null },
    })
    if (!response.ok) {
      passkeyError.value = problemMessage(problem, 'The passkey was not accepted.')
      return
    }
    passkeyAdded.value = true
    passkeyName.value = ''
  } catch {
    passkeyError.value = 'Adding the passkey was cancelled or failed.'
  }
}

onMounted(async () => {
  const [, providers] = await Promise.all([loadTokens(), session.raw.GET('/api/v1/auth/providers')])
  localLogin.value = providers.data?.some((provider) => provider.type === 'local') === true
})
</script>

<template>
  <section class="page">
    <h1>Security</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>

    <h2>API tokens</h2>
    <p class="muted">Tokens let Home Assistant and scripts call Marshal. A token can only get scopes you have yourself.</p>
    <div v-if="created" data-test="new-token" class="new-token" role="status">
      <p><strong>Copy the token now.</strong> It is shown only once.</p>
      <code>{{ created }}</code>
      <div class="actions">
        <button type="button" @click="copyToken">Copy</button>
        <button type="button" data-test="dismiss-token" @click="created = null">Done</button>
      </div>
    </div>
    <ul class="plain cards">
      <li v-for="token in tokens" :key="token.id" :data-token="token.id" class="card">
        <strong>{{ token.name }}</strong>
        <span v-if="token.revokedAt" class="badge unhealthy">revoked</span>
        <span v-else-if="token.readOnly" class="badge degraded">read-only</span>
        <div class="muted">{{ token.scopes.join(', ') }}</div>
        <div class="muted">
          created {{ formatDateTime(token.createdAt) }}<template v-if="token.expiresAt"> · expires {{ formatDateTime(token.expiresAt) }}</template>
        </div>
        <button v-if="!token.revokedAt && !token.readOnly" type="button" data-test="revoke" @click="revoke(token)">Revoke</button>
      </li>
    </ul>
    <form class="stack" data-test="create-token" @submit.prevent="createToken">
      <h3>New token</h3>
      <label>Name <input v-model="tokenName" name="tokenName" required maxlength="100" /></label>
      <fieldset>
        <legend>Scopes</legend>
        <label v-for="scope in user.current.value?.scopes ?? []" :key="scope" class="check">
          <input v-model="scopes" type="checkbox" name="scope" :value="scope" /> {{ scope }}
        </label>
      </fieldset>
      <label>Expires on (optional) <input v-model="expiresOn" name="expiresAt" type="date" /></label>
      <button type="submit" :disabled="!tokenName || scopes.length === 0">Create token</button>
    </form>

    <template v-if="localLogin && passkeys.supported()">
      <h2>Passkeys</h2>
      <form class="stack" data-test="add-passkey" @submit.prevent="addPasskey">
        <label>Name (optional) <input v-model="passkeyName" name="passkeyName" placeholder="e.g. Phone" /></label>
        <button type="submit">Add a passkey</button>
      </form>
      <p v-if="passkeyAdded" data-test="passkey-added" role="status">Passkey added. You can use it to sign in.</p>
      <p v-if="passkeyError" role="alert" class="error">{{ passkeyError }}</p>
    </template>
  </section>
</template>

<style scoped>
.cards {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.card {
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.card button {
  margin-top: 0.5rem;
}

.new-token {
  padding: 0.75rem;
  margin-bottom: 1rem;
  border: 2px solid var(--accent);
  border-radius: 8px;
}

.new-token code {
  display: block;
  overflow-wrap: anywhere;
}

.actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

fieldset {
  border: 1px solid var(--border);
  border-radius: 8px;
}

.check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.check input {
  width: auto;
  min-height: auto;
}
</style>
