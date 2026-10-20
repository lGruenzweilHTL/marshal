<script setup lang="ts">
import { Ban, Copy, Fingerprint, KeyRound, Plus } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { usePasskeys } from '../auth/passkeys'
import { useFormat } from '../i18n/locale'
import { useUserSession } from '../session'
import { AppButton, Badge, Banner, Card, Checkbox, CodeBlock, Dialog, ErrorBanner, Field, IconTile, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop, type Tone } from '../ui'
import SecurityTabs from './SecurityTabs.vue'

/** API tokens (the plaintext is shown once, right after creating) and, with local sign-in, passkeys. */
type Token = Schemas['ApiTokenResponse']

const api = useApi()
const user = useUserSession()
const passkeys = usePasskeys()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const tokens = useLoad(() => api.call((client) => client.GET('/api/v1/me/tokens')), { isEmpty: () => false })
const providers = useLoad(() => api.call((client) => client.GET('/api/v1/auth/providers')))
const localLogin = computed(() => providers.data.value?.some((provider) => provider.type === 'local') === true && passkeys.supported())
const ownScopes = computed(() => user.current.value?.scopes ?? [])
const scopeHelpKeys: Record<string, 'mcpRead' | 'mcpWrite'> = { 'mcp:read': 'mcpRead', 'mcp:write': 'mcpWrite' }
const scopeHelp = (scope: string) => (scopeHelpKeys[scope] ? t(`security.tokens.scopeHelp.${scopeHelpKeys[scope]}`) : undefined)

const draft = reactive({ name: '', scopes: [] as string[], expiresOn: '' })
const plaintext = shallowRef<string | null>(null)
const copied = shallowRef(false)

/** Expiry at the end of the chosen local day. */
function expiry(): string | null {
  if (!draft.expiresOn) return null
  const [year, month, day] = draft.expiresOn.split('-').map(Number) as [number, number, number]
  return new Date(year, month - 1, day, 23, 59, 59).toISOString()
}

const create = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/me/tokens', { body: { name: draft.name, scopes: draft.scopes, expiresAt: expiry(), kind: null } }))
  if (result.data) {
    plaintext.value = result.data.token
    copied.value = false
    Object.assign(draft, { name: '', scopes: [], expiresOn: '' })
    tokens.refresh()
  }
  return result
})

async function copy() {
  if (!plaintext.value) return
  await navigator.clipboard?.writeText(plaintext.value)
  copied.value = true
}

const revoking = shallowRef<Token | null>(null)
const revoke = useSubmit(async () => {
  const token = revoking.value!
  const result = await api.call((client) => client.DELETE('/api/v1/me/tokens/{id}', { params: { path: { id: token.id } } }))
  if (!result.error) {
    revoking.value = null
    tokens.refresh()
  }
  return result
})

const day = 86_400_000
function status(token: Token): { tone: Tone; label: string } {
  if (token.revokedAt) return { tone: 'neutral', label: t('security.tokens.status.revoked') }
  if (token.readOnly) return { tone: 'neutral', label: t('security.tokens.status.readOnly') }
  const left = token.expiresAt ? Math.ceil((new Date(token.expiresAt).getTime() - Date.now()) / day) : null
  if (left !== null && left <= 14) return { tone: 'warning', label: t('security.tokens.status.expiresSoon', { days: left }, left) }
  return { tone: 'success', label: t('security.tokens.status.active') }
}
const dates = (token: Token) =>
  [t('security.tokens.created', { date: format.value.date(token.createdAt) }), token.expiresAt ? t('security.tokens.expires', { date: format.value.date(token.expiresAt) }) : t('security.tokens.never')].join(' · ')

const passkeyName = shallowRef('')
const passkeyAdded = shallowRef(false)
const passkeyFailed = shallowRef(false)
const addPasskey = useSubmit(async () => {
  passkeyAdded.value = false
  passkeyFailed.value = false
  const options = await api.call((client) => client.POST('/api/v1/auth/local/passkeys/creation-options', { parseAs: 'text' }))
  if (options.error) return options
  let credentialJson: string
  try {
    credentialJson = await passkeys.create(options.data as string)
  } catch {
    passkeyFailed.value = true
    return { data: undefined }
  }
  const result = await api.call((client) => client.POST('/api/v1/auth/local/passkeys', { body: { credentialJson, name: passkeyName.value || null } }))
  if (!result.error) {
    passkeyAdded.value = true
    passkeyName.value = ''
  }
  return result
})
</script>

<template>
  <Page>
    <PageHeader :title="t('security.title')" :subtitle="desktop ? t('security.subtitle') : undefined" />
    <SecurityTabs />

    <Section :title="t('security.tokens.title')">
      <p class="t-body-s ink-2">{{ t('security.tokens.intro') }}</p>

      <Banner v-if="plaintext" tone="success" :title="t('security.tokens.plaintextTitle')" data-test="new-token">
        <div class="plaintext">
          <CodeBlock :code="plaintext" :label="t('security.tokens.title')" />
          <div class="buttons">
            <AppButton size="sm" :icon="Copy" @click="copy">{{ copied ? t('security.tokens.copied') : t('security.tokens.copy') }}</AppButton>
            <AppButton size="sm" variant="ghost" data-test="dismiss-token" @click="plaintext = null">{{ t('security.tokens.done') }}</AppButton>
          </div>
        </div>
      </Banner>

      <LoadBoundary :load="tokens">
        <template #default="{ data }">
          <p v-if="!data.length" class="t-body-s ink-3">{{ t('security.tokens.empty') }}</p>
          <ListCard v-else :label="t('security.tokens.title')">
            <ListRow v-for="token in data" :key="token.id" :data-token="token.id" :class="{ revoked: !!token.revokedAt }">
              <template #lead><IconTile :icon="KeyRound" :tone="status(token).tone" /></template>
              <template #title>
                <span class="token-name">{{ token.name }}</span>
                <Badge :tone="status(token).tone" :label="status(token).label" />
              </template>
              <template #subtitle>
                <span class="t-mono-s scopes">{{ token.scopes.join(', ') }}</span>
                <span class="dates">{{ dates(token) }}</span>
              </template>
              <template #trail>
                <AppButton
                  v-if="!token.revokedAt && !token.readOnly"
                  size="sm"
                  variant="ghost"
                  :icon="Ban"
                  :icon-only="!desktop"
                  :label="t('security.tokens.revoke')"
                  data-test="revoke"
                  @click="revoking = token"
                >
                  {{ t('security.tokens.revoke') }}
                </AppButton>
              </template>
            </ListRow>
          </ListCard>
        </template>
      </LoadBoundary>

      <Card :title="t('security.tokens.newTitle')">
        <form class="form" data-test="create-token" @submit.prevent="create.submit()">
          <Field v-model="draft.name" name="tokenName" :label="t('security.tokens.name')" :placeholder="t('security.tokens.namePlaceholder')" required />
          <fieldset class="scopes-field">
            <legend class="t-label">{{ t('security.tokens.scopes') }}</legend>
            <span class="t-body-s ink-3">{{ t('security.tokens.scopesHint') }}</span>
            <div class="scope-grid" :class="{ two: desktop }">
              <Checkbox v-for="scope in ownScopes" :key="scope" v-model="draft.scopes" name="scope" :value="scope" :label="scope" :description="scopeHelp(scope)" mono />
            </div>
          </fieldset>
          <Field v-model="draft.expiresOn" kind="date" name="expiresAt" :label="t('security.tokens.expiresOn')" :hint="t('security.tokens.expiresHint')" />
          <ErrorBanner v-if="create.error.value" :error="create.error.value" />
          <div>
            <AppButton type="submit" variant="primary" :icon="KeyRound" :disabled="!draft.name || !draft.scopes.length" :loading="create.busy.value">
              {{ t('security.tokens.create') }}
            </AppButton>
          </div>
        </form>
      </Card>
    </Section>

    <Section v-if="localLogin" :title="t('security.passkeys.title')">
      <Card>
        <div class="passkey-head">
          <IconTile :icon="Fingerprint" />
          <p class="t-body-s ink-2">{{ t('security.passkeys.intro') }}</p>
        </div>
        <form class="add-passkey" data-test="add-passkey" @submit.prevent="addPasskey.submit()">
          <Field v-model="passkeyName" name="passkeyName" :label="t('security.passkeys.name')" :placeholder="t('security.passkeys.namePlaceholder')" />
          <AppButton type="submit" :icon="Plus" :loading="addPasskey.busy.value">{{ t('security.passkeys.add') }}</AppButton>
        </form>
        <Banner v-if="passkeyAdded" tone="success" :title="t('security.passkeys.added')" data-test="passkey-added" />
        <Banner v-if="passkeyFailed" :title="t('security.passkeys.failed')" />
        <ErrorBanner v-if="addPasskey.error.value" :error="addPasskey.error.value" />
      </Card>
    </Section>

    <Dialog :open="!!revoking" :title="t('security.tokens.revokeTitle', { name: revoking?.name ?? '' })" :description="t('security.tokens.revokeBody')" @close="revoking = null">
      <ErrorBanner v-if="revoke.error.value" :error="revoke.error.value" />
      <template #actions>
        <AppButton @click="revoking = null">{{ t('actions.cancel') }}</AppButton>
        <AppButton variant="danger" data-test="confirm-revoke" :loading="revoke.busy.value" @click="revoke.submit()">{{ t('security.tokens.revoke') }}</AppButton>
      </template>
    </Dialog>
  </Page>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.plaintext {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin-top: var(--space-2);
  overflow-wrap: anywhere;
}
.buttons {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.token-name {
  margin-right: var(--space-2);
  font-weight: 600;
}
.revoked .token-name {
  color: var(--ink-3);
}
.scopes,
.dates {
  display: block;
  overflow-wrap: anywhere;
}
.scopes-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: 0;
  padding: 0;
  border: 0;
}
.scope-grid {
  display: grid;
  column-gap: var(--space-4);
}
.scope-grid.two {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.passkey-head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.add-passkey {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-2);
}
.add-passkey > :first-child {
  flex: 1 1 220px;
}
</style>
