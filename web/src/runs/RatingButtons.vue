<script setup lang="ts">
import { Check, ThumbsDown, ThumbsUp } from 'lucide-vue-next'
import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, ErrorBanner, Field } from '../ui'

type Rating = Schemas['RunRating']

/**
 * Thumbs up and down for one run. Thumbs down opens an optional note; clicking the current rating again removes it.
 * The rating is stored on the run; with "help Marshal learn" on, the server also makes an eval case of it.
 */
const props = defineProps<{ runId: string; rating?: Rating | null; note?: string | null }>()
const emit = defineEmits<{ rated: [rating: Rating | null, note: string | null, caseId: string | null] }>()

const api = useApi()
const { t } = useI18n()
const current = shallowRef<Rating | null>(props.rating ?? null)
const currentNote = shallowRef<string | null>(props.note ?? null)
const noting = shallowRef(false)
const draft = shallowRef(props.note ?? '')

const send = useSubmit(async (rating: Rating | null, note: string | null) => {
  const path = { params: { path: { id: props.runId } } }
  if (rating === null) {
    const result = await api.call((client) => client.DELETE('/api/v1/runs/{id}/rating', path))
    if (!result.error) {
      current.value = null
      currentNote.value = null
      emit('rated', null, null, null)
    }
    return result
  }
  const result = await api.call((client) => client.PUT('/api/v1/runs/{id}/rating', { ...path, body: { rating, note } }))
  if (result.data) {
    current.value = result.data.rating
    currentNote.value = result.data.note
    emit('rated', result.data.rating, result.data.note, result.data.caseId)
  }
  return result
})

function up() {
  noting.value = false
  void send.submit(current.value === 'up' ? null : 'up', null)
}

async function saveNote() {
  if ((await send.submit('down', draft.value.trim() || null)).data) noting.value = false
}

function down() {
  if (current.value === 'down') {
    noting.value = false
    void send.submit(null, null)
    return
  }
  draft.value = ''
  noting.value = true
  void send.submit('down', null)
}
</script>

<template>
  <div class="rating" data-test="rating">
    <div class="buttons" role="group" :aria-label="t('runs.rating.label')">
      <AppButton
        size="sm"
        variant="ghost"
        :icon="ThumbsUp"
        icon-only
        :label="t('runs.rating.up')"
        :aria-pressed="current === 'up'"
        :class="{ active: current === 'up' }"
        :disabled="send.busy.value"
        data-test="rate-up"
        @click="up"
      />
      <AppButton
        size="sm"
        variant="ghost"
        :icon="ThumbsDown"
        icon-only
        :label="t('runs.rating.down')"
        :aria-pressed="current === 'down'"
        :class="{ active: current === 'down' }"
        :disabled="send.busy.value"
        data-test="rate-down"
        @click="down"
      />
      <span v-if="current && currentNote && !noting" class="note" data-test="rating-note">{{ currentNote }}</span>
    </div>
    <form v-if="noting" class="note-form" @submit.prevent="saveNote">
      <Field v-model="draft" kind="textarea" :rows="2" name="ratingNote" :label="t('runs.rating.note')" :placeholder="t('runs.rating.notePlaceholder')" />
      <div class="actions">
        <AppButton size="sm" variant="ghost" @click="noting = false">{{ t('runs.rating.skip') }}</AppButton>
        <AppButton size="sm" type="submit" :icon="Check" :loading="send.busy.value" data-test="save-note">{{ t('runs.rating.saveNote') }}</AppButton>
      </div>
    </form>
    <ErrorBanner v-if="send.error.value" :error="send.error.value" />
  </div>
</template>

<style scoped>
.rating {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.buttons {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1);
  color: var(--ink-3);
}
.active {
  color: var(--accent-text);
  background: var(--accent-soft);
}
.note {
  color: var(--ink-2);
  font: var(--text-body-s);
  overflow-wrap: anywhere;
}
.note-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
