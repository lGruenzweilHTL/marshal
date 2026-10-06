<script setup lang="ts">
import { reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { useUserSession } from '../session'
import { AppButton, ErrorBanner, Field } from '../ui'
import AuthLayout from './AuthLayout.vue'

/** First start: create the owner account. A rejected setup keeps every value so only the problem needs fixing. */
const api = useApi()
const user = useUserSession()
const router = useRouter()
const { t } = useI18n()

const draft = reactive({ displayName: '', userName: '', password: '', repeat: '' })
const mismatch = shallowRef(false)

const create = useSubmit(async () => {
  const result = await api.call((client) =>
    client.POST('/api/v1/auth/local/setup', { body: { userName: draft.userName, displayName: draft.displayName || null, password: draft.password } }),
  )
  if (!result.error) {
    await user.refresh()
    await router.replace('/')
  }
  return result
})

function submit() {
  mismatch.value = draft.password !== draft.repeat
  if (!mismatch.value) void create.submit()
}
</script>

<template>
  <AuthLayout>
    <div class="titles">
      <h1 class="t-title-1">{{ t('auth.setup.title') }}</h1>
      <p class="ink-2">{{ t('auth.setup.intro') }}</p>
    </div>
    <form class="form" @submit.prevent="submit">
      <Field v-model="draft.displayName" name="displayName" :label="t('auth.setup.displayName')" autocomplete="name" />
      <Field v-model="draft.userName" name="userName" :label="t('auth.setup.userName')" autocomplete="username" required />
      <Field v-model="draft.password" kind="password" name="password" :label="t('auth.setup.password')" :hint="t('auth.setup.passwordHint')" autocomplete="new-password" required />
      <Field
        v-model="draft.repeat"
        kind="password"
        name="repeat"
        :label="t('auth.setup.repeat')"
        :error="mismatch ? t('auth.setup.mismatch') : null"
        autocomplete="new-password"
        required
      />
      <ErrorBanner v-if="create.error.value" :error="create.error.value" />
      <AppButton type="submit" variant="primary" size="lg" block :loading="create.busy.value">{{ t('auth.setup.submit') }}</AppButton>
    </form>
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
</style>
