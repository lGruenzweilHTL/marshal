<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { useUserSession } from '../session'

const session = useApi()
const user = useUserSession()
const router = useRouter()
const userName = ref('')
const displayName = ref('')
const password = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function submit() {
  busy.value = true
  error.value = null
  try {
    const { response, error: problem } = await session.api.POST('/api/v1/auth/local/setup', {
      body: { userName: userName.value, displayName: displayName.value || null, password: password.value },
    })
    if (!response.ok) {
      error.value = problemMessage(problem, 'The setup failed.')
      return
    }
    await user.refresh()
    await router.replace('/')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="auth-page">
    <h1>Set up Marshal</h1>
    <p>Create the owner account. You can add a passkey afterwards under Security.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <form class="stack" @submit.prevent="submit">
      <label>User name <input v-model="userName" name="userName" autocomplete="username" required /></label>
      <label>Display name <input v-model="displayName" name="displayName" autocomplete="name" /></label>
      <label>Password <input v-model="password" name="password" type="password" autocomplete="new-password" minlength="12" required /></label>
      <button type="submit" :disabled="busy">Create owner account</button>
    </form>
  </main>
</template>
