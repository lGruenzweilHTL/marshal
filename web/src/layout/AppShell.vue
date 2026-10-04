<script setup lang="ts">
import { LogOut } from 'lucide-vue-next'
import { computed, provide, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { useApi } from '../api/inject'
import { useLiveEvents } from '../live/liveEvents'
import { useUserSession } from '../session'
import { AppButton, Avatar, ListCard, ListRow, Logo, Sheet, SideNav, TabBar, useDesktop, type NavGroup, type TabItem } from '../ui'
import AppIcon from '../ui/AppIcon.vue'
import { createInboxCount, inboxCountKey } from './inboxCount'
import { groups, moreIcon, visibleDestinations } from './navigation'

const api = useApi()
const user = useUserSession()
const route = useRoute()
const { t } = useI18n()
const desktop = useDesktop()
const inbox = createInboxCount(api, useLiveEvents())
provide(inboxCountKey, inbox)
void inbox.refresh()

const moreOpen = shallowRef(false)
const admin = computed(() => user.current.value?.roles.includes('admin') ?? false)
const destinations = computed(() => visibleDestinations(admin.value))
const active = computed(() => (route.meta.nav as string | undefined) ?? '')
const countLabel = computed(() => t('shell.inboxCount', inbox.count.value))

const sideGroups = computed<NavGroup[]>(() =>
  groups
    .map((group) => ({
      id: group,
      label: t(`shell.groups.${group}`),
      items: destinations.value
        .filter((destination) => destination.group === group)
        .map((destination) => ({
          id: destination.id,
          label: t(`shell.nav.${destination.id}`),
          icon: destination.icon,
          to: destination.path,
          count: destination.id === 'inbox' ? inbox.count.value : undefined,
          countLabel: destination.id === 'inbox' ? countLabel.value : undefined,
          note: destination.admin ? t('shell.admin') : undefined,
        })),
    }))
    .filter((group) => group.items.length > 0),
)

const moreDestinations = computed(() => destinations.value.filter((destination) => !destination.tab))
const tabs = computed<TabItem[]>(() => [
  ...destinations.value
    .filter((destination) => destination.tab)
    .map((destination) => ({
      id: destination.id,
      label: t(`shell.nav.${destination.id}`),
      icon: destination.icon,
      to: destination.path,
      count: destination.id === 'inbox' ? inbox.count.value : undefined,
      countLabel: destination.id === 'inbox' ? countLabel.value : undefined,
    })),
  { id: 'more', label: t('shell.nav.more'), icon: moreIcon },
])
const activeTab = computed(() => (moreDestinations.value.some((destination) => destination.id === active.value) ? 'more' : active.value))

watch(
  () => route.fullPath,
  () => (moreOpen.value = false),
)

/** A real form post, so the browser follows the redirect to the IdP's end-session endpoint or the login page. */
async function logout() {
  const result = await api.antiforgeryToken()
  if (result.error) return
  const form = document.createElement('form')
  form.method = 'post'
  form.action = `${api.baseUrl}/api/v1/auth/logout`
  const token = document.createElement('input')
  token.type = 'hidden'
  token.name = '__RequestVerificationToken'
  token.value = result.data
  form.append(token)
  document.body.append(form)
  form.submit()
}
</script>

<template>
  <div class="shell" :class="desktop ? 'desktop' : 'phone'">
    <SideNav v-if="desktop" class="side" :groups="sideGroups" :active="active" :label="t('shell.label')">
      <template #brand>
        <RouterLink to="/" class="brand" :aria-label="t('shell.nav.home')"><Logo :size="17" /></RouterLink>
      </template>
      <template #account>
        <Avatar :name="user.current.value?.displayName ?? ''" />
        <span class="account">
          <span class="name" data-test="user">{{ user.current.value?.displayName }}</span>
          <span class="role">{{ admin ? t('shell.admin') : t('shell.user') }}</span>
        </span>
        <AppButton variant="ghost" size="sm" icon-only :icon="LogOut" :label="t('actions.signOut')" data-test="logout" @click="logout" />
      </template>
    </SideNav>
    <main class="main" :class="{ legacy: route.meta.legacy }">
      <RouterView />
    </main>
    <template v-if="!desktop">
      <TabBar class="tabs" :items="tabs" :active="activeTab" :label="t('shell.label')" @select="moreOpen = true" />
      <Sheet :open="moreOpen" :title="t('shell.nav.more')" @close="moreOpen = false">
        <div class="more">
          <ListCard v-for="group in sideGroups.filter((item) => item.id !== 'act')" :key="group.id" :title="group.label">
            <ListRow v-for="item in group.items" :key="item.id" :to="item.to" :title="item.label" :data-test="`more-${item.id}`">
              <template #lead><AppIcon :icon="item.icon" class="more-icon" /></template>
            </ListRow>
          </ListCard>
          <div class="more-account">
            <Avatar :name="user.current.value?.displayName ?? ''" />
            <span class="account">
              <span class="name" data-test="user">{{ user.current.value?.displayName }}</span>
              <span class="role">{{ admin ? t('shell.admin') : t('shell.user') }}</span>
            </span>
            <AppButton :icon="LogOut" data-test="logout" @click="logout">{{ t('actions.signOut') }}</AppButton>
          </div>
        </div>
      </Sheet>
    </template>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100dvh;
}
.desktop {
  display: grid;
  grid-template-columns: var(--sidenav-width) minmax(0, 1fr);
}
.side {
  position: sticky;
  top: 0;
}
.main {
  min-width: 0;
}
.phone .main {
  padding-bottom: calc(var(--tabbar-height) + var(--space-6) + env(safe-area-inset-bottom));
}
.tabs {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 20;
}
.brand {
  display: flex;
  align-items: center;
  height: 32px;
  color: var(--ink);
}
.account {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  min-width: 0;
}
.name {
  font: 600 14px/20px var(--font-sans);
}
.role {
  font: var(--text-body-s);
  color: var(--ink-3);
}
.more {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.more-icon {
  color: var(--ink-3);
}
.more-account {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: 0 var(--space-1);
}
</style>
