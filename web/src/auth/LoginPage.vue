<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { returnUrl } from './returnUrl'

const session = useApiSession()
const route = useRoute()
const providers = ref<Schemas['LoginProviderInfo'][]>([])
const setupAvailable = ref(false)
const failed = ref(false)
const target = computed(() => returnUrl(route.query))
const denied = computed(() => route.query.error === 'access_denied')

function serverLogin(provider: Schemas['LoginProviderInfo']): string {
  return `${session.baseUrl}/api/v1/auth/login/${encodeURIComponent(provider.name)}?returnUrl=${encodeURIComponent(target.value)}`
}

onMounted(async () => {
  const [list, setup] = await Promise.all([session.raw.GET('/api/v1/auth/providers'), session.raw.GET('/api/v1/auth/local/setup')])
  if (list.data) providers.value = list.data
  else failed.value = true
  setupAvailable.value = setup.data?.available === true
})
</script>

<template>
  <main class="auth-page legacy">
    <h1>Marshal</h1>
    <p v-if="denied" role="alert" class="error">Your account has no access to Marshal. Ask the administrator to add you to a Marshal group.</p>
    <p v-if="failed" role="alert" class="error">The login providers could not be loaded.</p>
    <ul class="providers">
      <li v-for="provider in providers" :key="provider.name">
        <RouterLink
          v-if="provider.type === 'local'"
          class="button"
          :data-provider="provider.name"
          :to="{ path: `/login/${provider.name}`, query: { returnUrl: target } }"
          >{{ provider.displayName }}</RouterLink
        >
        <a v-else class="button" :data-provider="provider.name" :href="serverLogin(provider)">Sign in with {{ provider.displayName }}</a>
      </li>
    </ul>
    <p v-if="setupAvailable">First start? <RouterLink to="/setup">Set up the owner account</RouterLink>.</p>
  </main>
</template>
