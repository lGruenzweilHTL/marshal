<script setup lang="ts">
import { Plus } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { AppButton, Page, PageHeader } from '../ui'
import ConversationList from './ConversationList.vue'

/** The conversation list as its own screen (phones open it from the chat header). */
const { t } = useI18n()
</script>

<template>
  <Page width="narrow">
    <PageHeader :title="t('chat.history.title')">
      <template #actions>
        <AppButton :icon="Plus" icon-only :label="t('chat.newChat')" to="/chat" data-test="new-chat" />
      </template>
    </PageHeader>
    <ConversationList />
  </Page>
</template>
