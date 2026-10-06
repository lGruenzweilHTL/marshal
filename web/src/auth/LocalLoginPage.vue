<script setup lang="ts">
import { KeyRound } from 'lucide-vue-next'
import { reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { useUserSession } from '../session'
import { AppButton, Banner, ErrorBanner, Field } from '../ui'
import AuthLayout from './AuthLayout.vue'
import { usePasskeys } from './passkeys'
import { returnUrl } from './returnUrl'

/** Local sign-in: a passkey first, the password as the alternative. Returns to the deep link on the same origin. */
const api = useApi()
const user = useUserSession()
const passkeys = usePasskeys()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const draft = reactive({ userName: '', password: '' })
const passkeyFailed = shallowRef(false)

async function signedIn() {
  await user.refresh()
  await router.replace(returnUrl(route.query))
}

const password = useSubmit(async () => {
  passkeyFailed.value = false
  const result = await api.call((client) => client.POST('/api/v1/auth/local/login', { body: { userName: draft.userName, password: draft.password } }))
  if (!result.error) await signedIn()
  return result
})

const passkey = useSubmit(async () => {
  passkeyFailed.value = false
  const options = await api.call((client) => client.POST('/api/v1/auth/local/passkey/options', { body: { userName: draft.userName || null }, parseAs: 'text' }))
  if (options.error) return options
  let credentialJson: string
  try {
    credentialJson = await passkeys.get(options.data as string)
  } catch {
    passkeyFailed.value = true
    return { data: undefined }
  }
  const result = await api.call((client) => client.POST('/api/v1/auth/local/passkey', { body: { credentialJson } }))
  if (!result.error) await signedIn()
  return result
})
</script>

<template>
  <AuthLayout>
    <div class="titles">
      <h1 class="t-title-1">{{ t('auth.local.title') }}</h1>
      <p class="ink-2">{{ t('auth.local.intro') }}</p>
    </div>

    <template v-if="passkeys.supported()">
      <AppButton variant="primary" size="lg" block :icon="KeyRound" data-test="passkey" :loading="passkey.busy.value" :disabled="password.busy.value" @click="passkey.submit()">
        {{ t('auth.local.passkey') }}
      </AppButton>
      <Banner v-if="passkeyFailed" :title="t('auth.local.passkeyFailed')" />
      <ErrorBanner v-if="passkey.error.value" :error="passkey.error.value" />
      <div class="or" role="separator"><span>{{ t('auth.local.or') }}</span></div>
    </template>

    <form class="form" @submit.prevent="password.submit()">
      <Field v-model="draft.userName" name="userName" :label="t('auth.local.userName')" autocomplete="username webauthn" required />
      <Field v-model="draft.password" kind="password" name="password" :label="t('auth.local.password')" autocomplete="current-password" required />
      <ErrorBanner v-if="password.error.value" :error="password.error.value" />
      <AppButton type="submit" :variant="passkeys.supported() ? 'secondary' : 'primary'" size="lg" block :loading="password.busy.value" :disabled="passkey.busy.value">
        {{ t('auth.local.submit') }}
      </AppButton>
    </form>

    <p class="t-body-s"><RouterLink :to="{ path: '/login', query: route.query }">{{ t('auth.local.other') }}</RouterLink></p>
  </AuthLayout>
</template>

<style scoped>
.titles {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.or {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  color: var(--ink-3);
  font: var(--text-body-s);
}
.or::before,
.or::after {
  content: '';
  flex-grow: 1;
  height: 1px;
  background: var(--line);
}
</style>
