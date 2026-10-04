<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useApiSession } from '../api/inject'
import { useUserSession } from '../session'
import { usePasskeys } from './passkeys'
import { returnUrl } from './returnUrl'

const session = useApiSession()
const user = useUserSession()
const passkeys = usePasskeys()
const route = useRoute()
const router = useRouter()
const userName = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function signedIn() {
  await user.refresh()
  await router.replace(returnUrl(route.query))
}

async function passwordLogin() {
  busy.value = true
  error.value = null
  try {
    const { response } = await session.raw.POST('/api/v1/auth/local/login', { body: { userName: userName.value, password: password.value } })
    if (response.ok) await signedIn()
    else error.value = response.status === 401 ? 'Wrong user name or password.' : 'Sign-in failed. Try again later.'
  } finally {
    busy.value = false
  }
}

async function passkeyLogin() {
  busy.value = true
  error.value = null
  try {
    const options = await session.raw.POST('/api/v1/auth/local/passkey/options', { body: { userName: userName.value || null }, parseAs: 'text' })
    if (!options.data) throw new Error('options')
    const credentialJson = await passkeys.get(options.data)
    const { response } = await session.raw.POST('/api/v1/auth/local/passkey', { body: { credentialJson } })
    if (response.ok) await signedIn()
    else error.value = 'The passkey was not accepted.'
  } catch {
    error.value = 'Passkey sign-in was cancelled or failed.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="auth-page">
    <h1>Sign in</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <form class="stack" @submit.prevent="passwordLogin">
      <label>User name <input v-model="userName" name="userName" autocomplete="username webauthn" required /></label>
      <label>Password <input v-model="password" name="password" type="password" autocomplete="current-password" required /></label>
      <button type="submit" :disabled="busy">Sign in</button>
    </form>
    <button v-if="passkeys.supported()" type="button" data-test="passkey" :disabled="busy" @click="passkeyLogin">Sign in with a passkey</button>
    <p><RouterLink :to="{ path: '/login', query: route.query }">Other sign-in options</RouterLink></p>
  </main>
</template>
