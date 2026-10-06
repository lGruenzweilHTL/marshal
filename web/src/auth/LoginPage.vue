<script setup lang="ts">
import { KeyRound, LogIn } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { AppButton, Banner, LoadBoundary } from '../ui'
import AuthLayout from './AuthLayout.vue'
import { returnUrl } from './returnUrl'

/** The sign-in choices: one button per login provider; local sign-in opens its own page. */
const api = useApi()
const route = useRoute()
const { t } = useI18n()

const providers = useLoad(() => api.call((client) => client.GET('/api/v1/auth/providers')))
const setup = useLoad(() => api.call((client) => client.GET('/api/v1/auth/local/setup')), { isEmpty: () => false })
const target = computed(() => returnUrl(route.query))

function serverLogin(provider: Schemas['LoginProviderInfo']): string {
  return `${api.baseUrl}/api/v1/auth/login/${encodeURIComponent(provider.name)}?returnUrl=${encodeURIComponent(target.value)}`
}
</script>

<template>
  <AuthLayout>
    <div class="titles">
      <h1 class="t-title-1">{{ t('auth.login.title') }}</h1>
      <p class="ink-2">{{ t('auth.login.intro') }}</p>
    </div>
    <Banner v-if="route.query.reason === 'expired'" tone="info" :title="t('auth.login.expired')" data-test="expired" />
    <Banner v-if="route.query.error === 'access_denied'" :title="t('auth.login.denied')" />

    <LoadBoundary :load="providers">
      <template #default="{ data }">
        <div class="providers">
          <template v-for="(provider, index) in data" :key="provider.name">
            <AppButton
              v-if="provider.type === 'local'"
              :to="{ path: `/login/${provider.name}`, query: { returnUrl: target } }"
              :variant="index === 0 ? 'primary' : 'secondary'"
              size="lg"
              block
              :icon="KeyRound"
              :data-provider="provider.name"
            >
              {{ provider.displayName }}
            </AppButton>
            <AppButton
              v-else
              :href="serverLogin(provider)"
              :variant="index === 0 ? 'primary' : 'secondary'"
              size="lg"
              block
              :icon="LogIn"
              :data-provider="provider.name"
            >
              {{ t('auth.login.with', { provider: provider.displayName }) }}
            </AppButton>
          </template>
        </div>
      </template>
    </LoadBoundary>

    <p v-if="setup.data.value?.available" class="t-body-s ink-2">
      {{ t('auth.login.setup') }} <RouterLink to="/setup">{{ t('auth.login.setupLink') }}</RouterLink>
    </p>
  </AuthLayout>
</template>

<style scoped>
.titles {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.providers {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
</style>
