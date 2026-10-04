<script setup lang="ts">
import { onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import type { ApiError } from '../api/errors'
import { useUserSession } from '../session'
import { LoadState, Logo } from '../ui'

/** Full-page error when the account could not be loaded at start (any `/me` failure except 401). */
const user = useUserSession()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const state = shallowRef<'loading' | 'error'>('loading')
const error = shallowRef<ApiError | null>(null)

async function retry() {
  state.value = 'loading'
  const result = await user.load()
  if (result.error) {
    error.value = result.error
    state.value = 'error'
    return
  }
  const target = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : '/'
  await router.replace(result.data ? target : { path: '/login', query: { returnUrl: target } })
}

onMounted(retry)
</script>

<template>
  <main class="session-error">
    <Logo :size="20" />
    <LoadState :state="state" :title="t('shell.accountFailed')" :error="error" size="page" @retry="retry" />
  </main>
</template>

<style scoped>
.session-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-6);
  max-width: 520px;
  margin: 0 auto;
  padding: var(--space-12) var(--page-gutter);
}
</style>
