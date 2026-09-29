<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useUserSession } from '../session'

const links = [
  { to: '/', label: 'Dashboard' },
  { to: '/chat', label: 'Chat' },
  { to: '/memory', label: 'Memory' },
  { to: '/printers', label: 'Printer' },
  { to: '/mail/accounts', label: 'Mail' },
  { to: '/runs', label: 'Runs' },
  { to: '/cost', label: 'Cost' },
  { to: '/security', label: 'Security' },
  { to: '/secrets', label: 'Secrets' },
  { to: '/preferences', label: 'Preferences' },
  { to: '/system', label: 'System' },
]

const adminLinks = [{ to: '/config', label: 'Config' }]

const session = useApi()
const user = useUserSession()
const route = useRoute()
const menuOpen = ref(false)
const visibleLinks = computed(() => (user.current.value?.roles.includes('admin') ? [...links, ...adminLinks] : links))

watch(
  () => route.fullPath,
  () => (menuOpen.value = false),
)

/** A real form post, so the browser follows the redirect to the IdP's end-session endpoint or the login page. */
async function logout() {
  const form = document.createElement('form')
  form.method = 'post'
  form.action = `${session.baseUrl}/api/v1/auth/logout`
  const token = document.createElement('input')
  token.type = 'hidden'
  token.name = '__RequestVerificationToken'
  token.value = await session.antiforgeryToken()
  form.append(token)
  document.body.append(form)
  form.submit()
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <button type="button" class="menu" data-test="menu" :aria-expanded="menuOpen" aria-controls="nav" @click="menuOpen = !menuOpen">
        Menu
      </button>
      <RouterLink to="/" class="brand">Marshal</RouterLink>
      <span class="user" data-test="user">{{ user.current.value?.displayName }}</span>
      <button type="button" data-test="logout" @click="logout">Log out</button>
    </header>
    <nav id="nav" :class="{ open: menuOpen }">
      <RouterLink v-for="link in visibleLinks" :key="link.to" :to="link.to">{{ link.label }}</RouterLink>
    </nav>
    <main class="content">
      <RouterView />
    </main>
  </div>
</template>
