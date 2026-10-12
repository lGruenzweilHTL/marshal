<script setup lang="ts">
import { MessageSquare, Mic, Plug } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { usePagedLoad } from '../api/usePagedLoad'
import { satelliteName } from '../format'
import { useFormat } from '../i18n/locale'
import { AppButton, ErrorBanner, IconTile, ListCard, ListRow, LoadBoundary } from '../ui'

/**
 * Earlier conversations, newest last turn first: web chats, voice requests (marked with the satellite or area) and API
 * turns. Each row opens the conversation at `/chat/{id}`.
 */
defineProps<{ activeId?: string | null }>()
const api = useApi()
const format = useFormat()
const { t } = useI18n()
const icons = { voice: Mic, web: MessageSquare, task: MessageSquare, api: Plug }

const conversations = usePagedLoad(
  (before, limit) => api.call((client) => client.GET('/api/v1/conversations', { params: { query: { before, limit } } })),
  (conversation) => conversation.lastTurnAt,
)

function mark(conversation: Schemas['ConversationSummary']): string | null {
  if (conversation.source === 'api') return t('chat.history.api')
  if (conversation.source !== 'voice') return null
  return conversation.satelliteId ? satelliteName(conversation.satelliteId) : conversation.areaId
}

defineExpose({ refresh: () => conversations.load.refresh() })
</script>

<template>
  <LoadBoundary
    :load="conversations.load"
    :error-title="t('chat.history.failed')"
    :empty-title="t('chat.history.empty.title')"
    :empty-body="t('chat.history.empty.body')"
    :empty-icon="MessageSquare"
  >
    <ListCard :label="t('chat.history.label')">
      <ListRow
        v-for="conversation in conversations.items.value"
        :key="conversation.id"
        :to="`/chat/${conversation.id}`"
        :chevron="false"
        :data-conversation="conversation.id"
        :aria-current="conversation.id === activeId ? 'page' : undefined"
        class="conversation"
        :class="{ active: conversation.id === activeId }"
      >
        <template #lead><IconTile :icon="icons[conversation.source]" :size="32" :data-test="conversation.source === 'voice' ? 'voice' : undefined" /></template>
        <template #title><span class="title">{{ conversation.title ?? t('chat.history.untitled') }}</span></template>
        <template #subtitle>
          <template v-if="mark(conversation)"><span data-test="mark">{{ mark(conversation) }}</span> · </template>
          <span class="num">{{ format.relative(conversation.lastTurnAt) }}</span>
        </template>
      </ListRow>
    </ListCard>
    <ErrorBanner v-if="conversations.more.error.value" :error="conversations.more.error.value" />
    <AppButton v-if="conversations.hasMore.value" variant="ghost" block data-test="more" :loading="conversations.more.busy.value" @click="conversations.more.submit()">
      {{ t('chat.history.more') }}
    </AppButton>
  </LoadBoundary>
</template>

<style scoped>
.title {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.conversation.active {
  background: var(--surface-2);
}
</style>
