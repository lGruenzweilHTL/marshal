<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { browserNavigation } from './browserNavigation'

type Account = Schemas['MailAccountItem']
type Provider = Schemas['MailOAuthProvider']

const strategies: Record<string, string> = { password: 'Password', oauth_google: 'Google (OAuth)', oauth_microsoft: 'Microsoft (OAuth)' }
const providerLabels: Record<string, string> = { google: 'Google', microsoft: 'Microsoft' }
const callbackErrors: Record<string, string> = {
  'mail.oauth_state_invalid': 'The sign-in expired or was started elsewhere. Please connect again.',
  access_denied: 'The sign-in was cancelled at the provider.',
}

const session = useApi()
const route = useRoute()
const accounts = ref<Account[]>([])
const providers = ref<Provider[]>([])
const error = ref<string | null>(null)
const status = ref<string | null>(null)

const imap = ref({ alias: '', host: '', port: 993, security: 'tls' as Schemas['MailSecurity'], userName: '', password: '', draftsFolder: '' })
const oauth = ref({ alias: '', email: '' })

if (typeof route.query.connected === 'string') status.value = `Connected ${route.query.connected}.`
if (typeof route.query.error === 'string') error.value = callbackErrors[route.query.error] ?? `The account could not be connected (${route.query.error}).`

async function load() {
  const [list, available] = await Promise.all([session.api.GET('/api/v1/mail/accounts'), session.api.GET('/api/v1/mail/oauth/providers')])
  if (list.data) accounts.value = list.data
  else error.value = 'The mail accounts could not be loaded.'
  if (available.data) providers.value = available.data
}

async function addImap() {
  error.value = null
  status.value = null
  const body = { ...imap.value, draftsFolder: imap.value.draftsFolder.trim() || null }
  const { response, error: problem } = await session.api.POST('/api/v1/mail/accounts', { body })
  imap.value.password = ''
  if (!response.ok) {
    error.value = problemMessage(problem, 'The account could not be added.')
    return
  }
  status.value = `Added ${imap.value.alias}.`
  imap.value = { alias: '', host: '', port: 993, security: 'tls', userName: '', password: '', draftsFolder: '' }
  await load()
}

async function connect(provider: string) {
  error.value = null
  status.value = null
  const { data, error: problem } = await session.api.POST('/api/v1/mail/oauth/{provider}/start', {
    params: { path: { provider } },
    body: { alias: oauth.value.alias, email: oauth.value.email },
  })
  if (!data) {
    error.value = problemMessage(problem, 'The sign-in could not be started.')
    return
  }
  browserNavigation.assign(data.authorizationUrl)
}

async function remove(account: Account) {
  if (!window.confirm(`Remove the mail account "${account.alias}"? Its stored credential is deleted too.`)) return
  const { response } = await session.api.DELETE('/api/v1/mail/accounts/{id}', { params: { path: { id: account.id } } })
  if (!response.ok) error.value = 'The account could not be removed.'
  await load()
}

onMounted(load)
</script>

<template>
  <section class="page">
    <h1>Mail accounts</h1>
    <p class="muted">Marshal reads mail and writes drafts. It never sends mail. Passwords and tokens are write-only.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="status" role="status">{{ status }}</p>

    <div v-if="accounts.length" class="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Alias</th>
            <th>Sign-in</th>
            <th>Server</th>
            <th>User</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="account in accounts" :key="account.id" :data-account="account.alias">
            <td>{{ account.alias }}</td>
            <td>{{ strategies[account.strategy] ?? account.strategy }}</td>
            <td>{{ account.host }}:{{ account.port }}</td>
            <td>{{ account.userName }}</td>
            <td><button type="button" data-test="delete" @click="remove(account)">Remove</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="muted">No mail accounts yet.</p>

    <form class="stack" data-test="connect-oauth" autocomplete="off" @submit.prevent>
      <h2>Connect with sign-in</h2>
      <label>Alias <input v-model="oauth.alias" name="oauthAlias" required pattern="[a-z0-9\-]{1,32}" title="lowercase letters, digits and -" /></label>
      <label>Address <input v-model="oauth.email" name="oauthEmail" type="email" required /></label>
      <div class="actions">
        <button
          v-for="provider in providers"
          :key="provider.name"
          type="button"
          :data-provider="provider.name"
          :disabled="!provider.configured"
          :title="provider.configured ? undefined : 'No OAuth client is configured for this provider on this instance.'"
          @click="connect(provider.name)"
        >
          Connect {{ providerLabels[provider.name] ?? provider.name }}
        </button>
      </div>
    </form>

    <form class="stack" data-test="add-imap" autocomplete="off" @submit.prevent="addImap">
      <h2>Add an IMAP account</h2>
      <label>Alias <input v-model="imap.alias" name="alias" required pattern="[a-z0-9\-]{1,32}" title="lowercase letters, digits and -" /></label>
      <label>Host <input v-model="imap.host" name="host" required /></label>
      <label>Port <input v-model.number="imap.port" name="port" type="number" min="1" max="65535" required /></label>
      <label
        >Security
        <select v-model="imap.security" name="security">
          <option value="tls">TLS</option>
          <option value="startTls">STARTTLS</option>
          <option value="none">None (trusted network only)</option>
        </select>
      </label>
      <label>User name <input v-model="imap.userName" name="userName" required /></label>
      <label>Password or app password <input v-model="imap.password" name="password" type="password" autocomplete="new-password" required /></label>
      <label>Drafts folder (optional) <input v-model="imap.draftsFolder" name="draftsFolder" placeholder="only if the server marks none" /></label>
      <button type="submit">Add</button>
    </form>
  </section>
</template>

<style scoped>
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
</style>
