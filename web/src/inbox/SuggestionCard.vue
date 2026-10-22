<script setup lang="ts">
import { Check, Lightbulb, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, CodeBlock, ErrorBanner, IconTile } from '../ui'

/**
 * One suggestion: its text, a snippet to copy and the action Accept runs, with why that action is confirm tier.
 * Accept runs the frozen action on the server; Dismiss runs nothing. Both lower or raise how often the detector speaks.
 */
const props = defineProps<{ suggestion: Schemas['SuggestionItem'] }>()
const emit = defineEmits<{ answered: [accepted: boolean, result: Schemas['SuggestionAnswerItem']] }>()
const api = useApi()
const { t } = useI18n()

const answer = useSubmit(async (accept: boolean) => {
  const params = { params: { path: { id: props.suggestion.id } } }
  const result = await api.call((client) =>
    accept ? client.POST('/api/v1/suggestions/{id}/accept', params) : client.POST('/api/v1/suggestions/{id}/dismiss', params),
  )
  if (result.data) emit('answered', accept, result.data)
  return result
})
</script>

<template>
  <article class="suggestion" :data-suggestion="suggestion.id">
    <div class="head">
      <IconTile :icon="Lightbulb" tone="accent" />
      <div class="body">
        <p class="t-body">{{ suggestion.text }}</p>
        <p v-if="suggestion.actionSummary" class="t-body-s ink-2">
          {{ t('inbox.suggestion.action') }} <span class="t-mono-s">{{ suggestion.actionSummary }}</span>
        </p>
      </div>
    </div>
    <CodeBlock v-if="suggestion.snippet" :code="suggestion.snippet.replace(/\n$/, '')" :label="t('inbox.suggestion.snippet')" />
    <div v-if="suggestion.actionReasons.length" class="reasons t-body-s" data-test="reasons">
      <span class="ink-2">{{ t('inbox.suggestion.confirms') }}</span>
      <ul>
        <li v-for="reason in suggestion.actionReasons" :key="reason">{{ reason }}</li>
      </ul>
    </div>
    <ErrorBanner v-if="answer.error.value" :error="answer.error.value" />
    <div class="actions">
      <AppButton size="sm" variant="ghost" :icon="X" data-test="dismiss" :disabled="answer.busy.value" @click="answer.submit(false)">
        {{ t('inbox.suggestion.dismiss') }}
      </AppButton>
      <AppButton size="sm" variant="primary" :icon="Check" data-test="accept" :loading="answer.busy.value" @click="answer.submit(true)">
        {{ t('inbox.suggestion.accept') }}
      </AppButton>
    </div>
  </article>
</template>

<style scoped>
.suggestion {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
  min-width: 0;
}
.head {
  display: flex;
  gap: var(--space-3);
  align-items: flex-start;
}
.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  min-width: 0;
}
.reasons ul {
  margin: var(--space-1) 0 0;
  padding-left: var(--space-5);
}
.actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
