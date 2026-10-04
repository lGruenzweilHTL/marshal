<script setup lang="ts">
import { House, SearchX } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { AppButton, LoadState, Page } from '../ui'

const { t } = useI18n()
</script>

<template>
  <Page width="narrow">
    <LoadState state="empty" :icon="SearchX" :title="t('shell.notFound.title')" :body="t('shell.notFound.body')" size="page">
      <AppButton to="/" :icon="House" class="home">{{ t('shell.notFound.home') }}</AppButton>
    </LoadState>
  </Page>
</template>

<style scoped>
.home {
  margin-top: var(--space-2);
}
</style>
