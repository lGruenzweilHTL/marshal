<script setup lang="ts">
import { Check, Trash2 } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, Banner, Card, CodeBlock, ErrorBanner, Field, KeyValueList, LoadBoundary, Page, PageHeader, useDesktop } from '../ui'
import { expectationTones } from './tones'

/** One eval case as recorded: input, history, turn context, tool calls and reply, with its editable note. */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

const evalCase = useLoad(() => api.call((client) => client.GET('/api/v1/evals/cases/{id}', { params: { path: { id } } })), { isEmpty: () => false })
const data = computed(() => evalCase.data.value ?? null)
const note = shallowRef('')
const noteSaved = shallowRef(false)
watch(data, (value) => value && (note.value = value.note ?? ''), { immediate: true })

const saveNote = useSubmit(async () => {
  noteSaved.value = false
  const result = await api.call((client) => client.PUT('/api/v1/evals/cases/{id}/note', { params: { path: { id } }, body: { note: note.value.trim() || null } }))
  if (!result.error) noteSaved.value = true
  return result
})

const remove = useSubmit(async () => {
  const result = await api.call((client) => client.DELETE('/api/v1/evals/cases/{id}', { params: { path: { id } } }))
  if (!result.error) void router.push('/system/evals')
  return result
})
</script>

<template>
  <Page width="wide">
    <PageHeader :title="data ? data.input : t('evals.case.title')" back="/system/evals" :back-label="t('evals.title')">
      <template v-if="data" #subtitle>
        <span class="headline">
          <Badge :tone="expectationTones[data.expectation]" :label="t(`evals.expectation.${data.expectation}`)" />
          <Badge v-if="data.incomplete" tone="warning" :label="t('evals.incomplete')" data-test="incomplete" />
        </span>
      </template>
      <template v-if="data" #actions>
        <AppButton variant="danger" :icon="Trash2" :loading="remove.busy.value" data-test="delete" @click="remove.submit()">{{ t('evals.case.delete') }}</AppButton>
      </template>
    </PageHeader>

    <LoadBoundary :load="evalCase" size="page">
      <template v-if="data">
        <Banner v-if="data.incomplete" tone="warning" :title="t('evals.incomplete')">{{ t('evals.incompleteHint') }}</Banner>
        <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
        <div class="layout" :class="{ split: desktop }">
          <div class="column">
            <Card :title="t('evals.case.history')">
              <p v-if="!data.history?.length" class="t-body-s ink-3">{{ t('evals.case.noHistory') }}</p>
              <ol v-else class="history">
                <li v-for="(message, index) in data.history" :key="index" :class="message.role">
                  <span class="t-overline">{{ t(`evals.case.roles.${message.role === 'user' ? 'user' : 'assistant'}`) }}</span>
                  <p>{{ message.text }}</p>
                </li>
              </ol>
            </Card>
            <Card :title="t('evals.case.input')"><p class="text" data-test="input">{{ data.input }}</p></Card>
            <Card :title="t('evals.case.turnContext')">
              <CodeBlock v-if="data.turnContext" :code="data.turnContext" :label="t('evals.case.turnContext')" />
              <p v-else class="t-body-s ink-3">{{ t('evals.case.noTurnContext') }}</p>
            </Card>
            <Card :title="t('evals.case.toolCalls')">
              <p v-if="!data.toolCalls?.length" class="t-body-s ink-3">{{ t('evals.case.noToolCalls') }}</p>
              <div v-for="(call, index) in data.toolCalls" :key="index" class="call" data-test="tool-call">
                <span class="tool t-mono-s">{{ call.tool }} <Badge v-if="call.untrusted" tone="warning" :label="t('evals.case.untrusted')" /></span>
                <CodeBlock :code="call.arguments" :label="call.tool" />
                <CodeBlock v-if="call.result" :code="call.result" :label="t('runs.detail.result')" />
                <p v-if="call.error" class="error">{{ call.error }}</p>
              </div>
            </Card>
            <Card :title="t('evals.case.reply')"><p class="text">{{ data.reply ?? '–' }}</p></Card>
          </div>

          <div class="column">
            <Card :title="t('evals.case.note')">
              <form class="note" @submit.prevent="saveNote.submit()">
                <Field v-model="note" kind="textarea" :rows="4" name="note" :label="t('evals.case.note')" hide-label :hint="t('evals.case.noteHint')" />
                <ErrorBanner v-if="saveNote.error.value" :error="saveNote.error.value" />
                <Banner v-if="noteSaved" tone="success" :title="t('evals.case.noteSaved')" data-test="note-saved" />
                <div><AppButton type="submit" :icon="Check" :loading="saveNote.busy.value" data-test="save-note">{{ t('evals.case.saveNote') }}</AppButton></div>
              </form>
            </Card>
            <Card>
              <KeyValueList
                :items="[
                  { key: 'run', label: t('evals.case.sourceRun') },
                  { key: 'recorded', label: t('evals.case.recordedAt'), value: format.dateTime(data.recordedAt) },
                  { key: 'language', label: t('evals.case.language'), value: [data.language, data.inputMode].filter(Boolean).join(' · ') },
                ]"
              >
                <template #run><RouterLink :to="`/runs/${data.sourceRunId}`" class="t-mono-s">{{ data.sourceRunId }}</RouterLink></template>
              </KeyValueList>
            </Card>
          </div>
        </div>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.headline {
  display: inline-flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.layout,
.column,
.note {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: var(--space-6);
  align-items: start;
}
.history {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin: 0;
  padding: 0;
  list-style: none;
}
.history p,
.text {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.call {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.call + .call {
  padding-top: var(--space-3);
  border-top: 1px solid var(--line);
}
.tool {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font-weight: 600;
}
.error {
  color: var(--danger);
  font: var(--text-body-s);
}
</style>
