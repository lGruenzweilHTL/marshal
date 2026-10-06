<script setup lang="ts">
import { KeyRound, Trash2 } from 'lucide-vue-next'
import { computed, nextTick, reactive, shallowRef, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import SecurityTabs from '../security/SecurityTabs.vue'
import { useUserSession } from '../session'
import { AppButton, Badge, Banner, Card, Dialog, ErrorBanner, Field, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop } from '../ui'

/** Secrets by name: values are write-only. Unresolved references from `/config` are flagged with where they are used. */
type Secret = Schemas['SecretItem']

const api = useApi()
const user = useUserSession()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const overview = useLoad(() => api.call((client) => client.GET('/api/v1/me/secrets')), { isEmpty: () => false })
const missing = computed(() => overview.data.value?.unresolved.length ?? 0)
const isAdmin = computed(() => user.current.value?.roles.includes('admin') === true)

const draft = reactive({ name: '', value: '', scope: 'user' as 'user' | 'instance' })
const stored = shallowRef<string | null>(null)
const form = useTemplateRef<HTMLFormElement>('form')

async function prefill(name: string) {
  draft.name = name
  await nextTick()
  form.value?.querySelector<HTMLInputElement>('input[name="secretValue"]')?.focus()
}

const store = useSubmit(async () => {
  stored.value = null
  const name = draft.name
  const body = { value: draft.value }
  const path = { params: { path: { name } } }
  const result = await api.call((client) =>
    draft.scope === 'instance' ? client.PUT('/api/v1/secrets/instance/{name}', { ...path, body }) : client.PUT('/api/v1/me/secrets/{name}', { ...path, body }),
  )
  draft.value = ''
  if (!result.error) {
    stored.value = name
    draft.name = ''
    overview.refresh()
  }
  return result
})

const deleting = shallowRef<Secret | null>(null)
const remove = useSubmit(async () => {
  const secret = deleting.value!
  const path = { params: { path: { name: secret.name } } }
  const result = await api.call((client) =>
    secret.scope === 'instance' ? client.DELETE('/api/v1/secrets/instance/{name}', path) : client.DELETE('/api/v1/me/secrets/{name}', path),
  )
  if (!result.error) {
    deleting.value = null
    overview.refresh()
  }
  return result
})

const details = (secret: Secret) =>
  [
    t(`security.secrets.scope.${secret.scope === 'instance' ? 'instance' : 'user'}`),
    secret.updatedAt ? t('security.secrets.updated', { date: format.value.date(secret.updatedAt) }) : null,
  ]
    .filter(Boolean)
    .join(' · ')
</script>

<template>
  <Page>
    <PageHeader :title="t('security.title')" :subtitle="desktop ? t('security.subtitle') : undefined" />
    <SecurityTabs :missing="missing" />
    <p class="t-body-s ink-2">{{ t('security.secrets.intro') }}</p>

    <LoadBoundary :load="overview">
      <template #default="{ data }">
        <Banner v-if="data.unresolved.length" tone="warning" :title="t('security.secrets.unresolved', { count: data.unresolved.length }, data.unresolved.length)" data-test="unresolved">
          <ul class="unresolved">
            <li v-for="entry in data.unresolved" :key="entry.name" :data-unresolved="entry.name">
              <span class="t-mono-s name">{{ entry.name }}</span>
              <span class="where">{{ t('security.secrets.referencedBy', { where: entry.referencedBy.join('; ') }) }}</span>
              <AppButton size="sm" data-test="set" @click="prefill(entry.name)">{{ t('security.secrets.setValue') }}</AppButton>
            </li>
          </ul>
        </Banner>

        <Section :title="t('security.secrets.list')" :count="data.secrets.length">
          <p v-if="!data.secrets.length" class="t-body-s ink-3">{{ t('security.secrets.empty') }}</p>
          <ListCard v-else :label="t('security.secrets.list')">
            <ListRow v-for="secret in data.secrets" :key="`${secret.scope}/${secret.name}`" :data-secret="`${secret.scope}/${secret.name}`" :subtitle="details(secret)">
              <template #title>
                <span class="t-mono name">{{ secret.name }}</span>
                <Badge :tone="secret.origin === 'database' ? 'info' : 'neutral'" :label="t(`security.secrets.origin.${secret.origin}`)" />
              </template>
              <template #trail>
                <span v-if="secret.readOnly" class="t-body-s ink-3">{{ t('security.secrets.readOnly') }}</span>
                <AppButton v-else size="sm" variant="ghost" :icon="Trash2" data-test="delete" @click="deleting = secret">{{ t('security.secrets.delete') }}</AppButton>
              </template>
            </ListRow>
          </ListCard>
        </Section>
      </template>
    </LoadBoundary>

    <Card :title="t('security.secrets.setTitle')">
      <form ref="form" class="form" data-test="set-secret" autocomplete="off" @submit.prevent="store.submit()">
        <Field v-model="draft.name" name="secretName" :label="t('security.secrets.name')" :hint="t('security.secrets.nameHint')" placeholder="moonraker_voron" mono required />
        <Field v-model="draft.value" kind="password" name="secretValue" :label="t('security.secrets.value')" :hint="t('security.secrets.valueHint')" autocomplete="new-password" required />
        <Field v-if="isAdmin" v-model="draft.scope" kind="select" name="secretScope" :label="t('security.secrets.scopeLabel')">
          <option value="user">{{ t('security.secrets.scopes.user') }}</option>
          <option value="instance">{{ t('security.secrets.scopes.instance') }}</option>
        </Field>
        <ErrorBanner v-if="store.error.value" :error="store.error.value" />
        <Banner v-if="stored" tone="success" :title="t('security.secrets.stored', { name: stored })" />
        <div>
          <AppButton type="submit" variant="primary" :icon="KeyRound" :loading="store.busy.value">{{ t('security.secrets.store') }}</AppButton>
        </div>
      </form>
    </Card>

    <Dialog :open="!!deleting" :title="t('security.secrets.deleteTitle', { name: deleting?.name ?? '' })" :description="t('security.secrets.deleteBody')" @close="deleting = null">
      <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
      <template #actions>
        <AppButton @click="deleting = null">{{ t('actions.cancel') }}</AppButton>
        <AppButton variant="danger" :icon="Trash2" data-test="confirm-delete" :loading="remove.busy.value" @click="remove.submit()">{{ t('security.secrets.delete') }}</AppButton>
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
.unresolved {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin: var(--space-2) 0 0;
  padding: 0;
  list-style: none;
}
.unresolved li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1) var(--space-3);
}
.where {
  flex: 1 1 200px;
  overflow-wrap: anywhere;
}
.name {
  margin-right: var(--space-2);
  font-weight: 600;
  overflow-wrap: anywhere;
}
</style>
