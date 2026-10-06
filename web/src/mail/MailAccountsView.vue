<script setup lang="ts">
import { KeyRound, Mail, Plus, Trash2 } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, Banner, Card, Dialog, ErrorBanner, Field, IconTile, KeyValueList, LoadBoundary, Page, PageHeader, useDesktop } from '../ui'
import { browserNavigation } from './browserNavigation'

/** Mail accounts Marshal may read and draft in: IMAP with a password or OAuth sign-in. Credentials are write-only. */
type Account = Schemas['MailAccountItem']

const api = useApi()
const route = useRoute()
const desktop = useDesktop()
const format = useFormat()
const { t, te } = useI18n()

const accounts = useLoad(() => api.call((client) => client.GET('/api/v1/mail/accounts')), { isEmpty: (list) => list.length === 0 })
const providers = useLoad(() => api.call((client) => client.GET('/api/v1/mail/oauth/providers')))

/** The result of the OAuth callback, from the query the server redirects back with. */
const callback = computed(() => {
  const { connected, error } = route.query
  if (typeof connected === 'string') return { tone: 'success' as const, text: t('mail.connect.connected', { alias: connected }) }
  if (typeof error !== 'string') return null
  const known: Record<string, string> = { 'mail.oauth_state_invalid': 'mail.connect.expired', access_denied: 'mail.connect.denied' }
  return { tone: 'danger' as const, text: known[error] ? t(known[error]) : t('mail.connect.failed', { code: error }) }
})
const added = shallowRef<string | null>(null)

const emptyImap = () => ({ alias: '', host: '', port: 993 as number | string, security: 'tls' as Schemas['MailSecurity'], userName: '', password: '', draftsFolder: '' })
const imap = reactive(emptyImap())
const addImap = useSubmit(async () => {
  added.value = null
  const body = { ...imap, port: Number(imap.port), draftsFolder: imap.draftsFolder.trim() || null }
  const result = await api.call((client) => client.POST('/api/v1/mail/accounts', { body }))
  imap.password = ''
  if (!result.error) {
    added.value = body.alias
    Object.assign(imap, emptyImap())
    accounts.refresh()
  }
  return result
})

const oauth = reactive({ alias: '', email: '' })
const connect = useSubmit(async (provider: string) => {
  const result = await api.call((client) =>
    client.POST('/api/v1/mail/oauth/{provider}/start', { params: { path: { provider } }, body: { alias: oauth.alias, email: oauth.email } }),
  )
  if (result.data) browserNavigation.assign(result.data.authorizationUrl)
  return result
})

const removing = shallowRef<Account | null>(null)
const remove = useSubmit(async () => {
  const account = removing.value!
  const result = await api.call((client) => client.DELETE('/api/v1/mail/accounts/{id}', { params: { path: { id: account.id } } }))
  if (!result.error) {
    removing.value = null
    accounts.refresh()
  }
  return result
})

const providerName = (name: string) => (te(`mail.provider.${name}`) ? t(`mail.provider.${name}`) : name)
const strategy = (account: Account) => (te(`mail.strategy.${account.strategy}`) ? t(`mail.strategy.${account.strategy}`) : account.strategy)
const details = (account: Account) => [
  { key: 'server', label: t('mail.server'), value: `${account.host}:${account.port}`, mono: true },
  { key: 'security', label: t('mail.securityLabel'), value: t(`mail.security.${account.security}`) },
  { key: 'drafts', label: t('mail.draftsFolder'), value: account.draftsFolder ?? t('mail.draftsDefault'), mono: !!account.draftsFolder },
  { key: 'added', label: t('mail.added'), value: format.value.date(account.createdAt) },
]
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('mail.title')" :subtitle="desktop ? t('mail.subtitle') : undefined" />
    <Banner tone="info" :title="t('mail.noSend.title')" :body="t('mail.noSend.body')" data-test="no-send" />
    <Banner v-if="callback" :tone="callback.tone" :title="callback.text" data-test="callback" />

    <div class="layout" :class="{ two: desktop }">
      <div class="column">
        <LoadBoundary :load="accounts" :empty-title="t('mail.empty.title')" :empty-body="t('mail.empty.body')" :empty-icon="Mail">
          <template #default="{ data }">
            <Card v-for="account in data" :key="account.id" :data-account="account.alias">
              <div class="head">
                <IconTile :icon="Mail" tone="info" :size="40" />
                <div class="names">
                  <h2 class="alias">{{ account.alias }}</h2>
                  <span class="t-mono-s ink-2 user">{{ account.userName }}</span>
                </div>
                <Badge :tone="account.strategy === 'password' ? 'neutral' : 'info'" :label="strategy(account)" />
              </div>
              <KeyValueList :items="details(account)" />
              <div>
                <AppButton size="sm" variant="ghost" :icon="Trash2" data-test="delete" @click="removing = account">{{ t('mail.remove') }}</AppButton>
              </div>
            </Card>
          </template>
        </LoadBoundary>

        <Card :title="t('mail.connect.title')">
          <form class="form" data-test="connect-oauth" autocomplete="off" @submit.prevent>
            <p class="ink-2 t-body-s">{{ t('mail.connect.intro') }}</p>
            <Field v-model="oauth.alias" name="oauthAlias" :label="t('mail.connect.alias')" :hint="t('mail.connect.aliasHint')" placeholder="work" required />
            <Field v-model="oauth.email" kind="email" name="oauthEmail" :label="t('mail.connect.email')" placeholder="you@example.com" required />
            <ErrorBanner v-if="connect.error.value" :error="connect.error.value" />
            <div class="buttons">
              <AppButton
                v-for="provider in providers.data.value ?? []"
                :key="provider.name"
                :icon="KeyRound"
                :data-provider="provider.name"
                :disabled="!provider.configured"
                :title="provider.configured ? undefined : t('mail.connect.notConfigured', { provider: providerName(provider.name) })"
                :loading="connect.busy.value"
                @click="connect.submit(provider.name)"
              >
                {{ t('mail.connect.button', { provider: providerName(provider.name) }) }}
              </AppButton>
            </div>
          </form>
        </Card>
      </div>

      <Card :title="t('mail.imap.title')">
        <form class="form" data-test="add-imap" autocomplete="off" @submit.prevent="addImap.submit()">
          <Field v-model="imap.alias" name="alias" :label="t('mail.imap.alias')" :hint="t('mail.connect.aliasHint')" placeholder="private" required />
          <div class="server" :class="{ two: desktop }">
            <Field v-model="imap.host" name="host" :label="t('mail.imap.host')" placeholder="imap.example.com" mono required />
            <Field v-model="imap.port" kind="number" name="port" :label="t('mail.imap.port')" mono required />
          </div>
          <Field v-model="imap.security" kind="select" name="security" :label="t('mail.imap.security')">
            <option value="tls">{{ t('mail.security.tls') }}</option>
            <option value="startTls">{{ t('mail.security.startTls') }}</option>
            <option value="none">{{ t('mail.security.none') }}</option>
          </Field>
          <Field v-model="imap.userName" name="userName" :label="t('mail.imap.userName')" placeholder="you@example.com" required />
          <Field v-model="imap.password" kind="password" name="password" :label="t('mail.imap.password')" :hint="t('mail.imap.passwordHint')" autocomplete="new-password" required />
          <Field v-model="imap.draftsFolder" name="draftsFolder" :label="t('mail.imap.draftsFolder')" :hint="t('mail.imap.draftsHint')" mono />
          <ErrorBanner v-if="addImap.error.value" :error="addImap.error.value" />
          <Banner v-if="added" tone="success" :title="t('mail.imap.added', { alias: added })" />
          <div><AppButton type="submit" variant="primary" :icon="Plus" :loading="addImap.busy.value">{{ t('mail.imap.add') }}</AppButton></div>
        </form>
      </Card>
    </div>

    <Dialog :open="!!removing" :title="t('mail.removeTitle', { alias: removing?.alias ?? '' })" :description="t('mail.removeBody')" @close="removing = null">
      <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
      <template #actions>
        <AppButton @click="removing = null">{{ t('actions.cancel') }}</AppButton>
        <AppButton variant="danger" :icon="Trash2" data-test="confirm-delete" :loading="remove.busy.value" @click="remove.submit()">{{ t('mail.remove') }}</AppButton>
      </template>
    </Dialog>
  </Page>
</template>

<style scoped>
.layout,
.column,
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.layout.two {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: var(--space-5);
  align-items: start;
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.names {
  display: flex;
  flex-grow: 1;
  flex-direction: column;
  min-width: 0;
}
.alias {
  font: var(--text-title-3);
}
.user {
  overflow-wrap: anywhere;
}
.server {
  display: grid;
  gap: var(--space-4);
}
.server.two {
  grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
}
.buttons {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>
