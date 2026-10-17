<script setup lang="ts">
import { ExternalLink, KeyRound } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, AppIcon, Checkbox, ErrorBanner, Field } from '../ui'
import { inputAnswer, inputFields } from './inputSchema'

/**
 * An MCP server's input request: a form generated from the requested schema, or the server's page with "Done". The
 * message, field labels and URL are server text, shown as plain text; the URL opens only when the user clicks it.
 * Only this form answers: the model never sees the request.
 */
type Request = Schemas['InputRequestItem']

const props = withDefaults(defineProps<{ request: Request; density?: 'default' | 'compact' }>(), { density: 'default' })
const emit = defineEmits<{ settled: [result: 'answered' | 'declined'] }>()

const api = useApi()
const { t } = useI18n()

const fields = computed(() => (props.request.mode === 'form' ? inputFields(props.request.requestedSchema) : []))
const values = reactive<Record<string, string | number | boolean | null>>(Object.fromEntries(fields.value.map((field) => [field.name, field.initial])))
const outcome = shallowRef<'answered' | 'declined' | null>(null)
const open = computed(() => props.request.status === 'pending' && outcome.value === null)

const answer = useSubmit(async () => {
  const content = props.request.mode === 'form' ? inputAnswer(fields.value, values) : null
  const result = await api.call((client) => client.POST('/api/v1/approvals/inputs/{id}', { params: { path: { id: props.request.id } }, body: { content } }))
  if (!result.error) settle('answered')
  return result
})

const decline = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/approvals/inputs/{id}/decline', { params: { path: { id: props.request.id } } }))
  if (!result.error) settle('declined')
  return result
})

function settle(result: 'answered' | 'declined') {
  outcome.value = result
  emit('settled', result)
}

const error = computed(() => answer.error.value ?? decline.error.value)
const busy = computed(() => answer.busy.value || decline.busy.value)
</script>

<template>
  <article class="input" :class="density" :data-input="request.id">
    <header class="head">
      <AppIcon :icon="KeyRound" :size="18" />
      <span class="t-label">{{ t('inbox.input.label', { server: request.serverId }) }}</span>
    </header>
    <p class="t-body-s ink-3">{{ t('inbox.input.from', { server: request.serverId, tool: request.tool }) }}</p>
    <p class="message" data-test="message">{{ request.message }}</p>

    <p v-if="outcome" class="t-body-s result" role="status" data-test="outcome">{{ t(`inbox.input.${outcome}`) }}</p>
    <p v-else-if="!open" class="t-body-s ink-3" data-test="closed">{{ t('inbox.input.closed', { status: t(`inbox.input.status.${request.status}`) }) }}</p>

    <form v-else @submit.prevent="answer.submit()">
      <template v-if="request.mode === 'form'">
        <template v-for="field in fields" :key="field.name">
          <Checkbox v-if="field.kind === 'boolean'" v-model="values[field.name] as boolean" :label="field.label" :description="field.description" :name="field.name" />
          <Field
            v-else-if="field.kind === 'choice'"
            v-model="values[field.name] as string"
            kind="select"
            :label="field.label"
            :hint="field.description"
            :required="field.required"
            :name="field.name"
          >
            <option v-for="option in field.options" :key="option.value" :value="option.value">{{ option.label }}</option>
          </Field>
          <Field
            v-else
            v-model="values[field.name] as string | number | null"
            :kind="field.kind === 'number' ? 'number' : 'text'"
            :label="field.label"
            :hint="field.description"
            :required="field.required"
            :name="field.name"
          />
        </template>
      </template>
      <template v-else-if="request.url">
        <a class="open" :href="request.url" target="_blank" rel="noopener noreferrer nofollow" data-test="open">
          <AppIcon :icon="ExternalLink" :size="16" />
          <span>{{ t('inbox.input.open') }}</span>
        </a>
        <span class="url t-mono-s ink-3">{{ request.url }}</span>
        <p class="t-body-s ink-3">{{ t('inbox.input.openHint') }}</p>
      </template>

      <ErrorBanner v-if="error" :error="error" />
      <div class="actions">
        <AppButton type="submit" variant="primary" size="sm" :loading="answer.busy.value" :disabled="busy" data-test="answer">
          {{ request.mode === 'form' ? t('inbox.input.send') : t('inbox.input.done') }}
        </AppButton>
        <AppButton variant="ghost" size="sm" :loading="decline.busy.value" :disabled="busy" data-test="decline" @click="decline.submit()">{{ t('inbox.input.decline') }}</AppButton>
      </div>
    </form>
  </article>
</template>

<style scoped>
.input {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
}
.input.compact {
  padding: var(--space-3);
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  color: var(--accent);
}
.message {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
form {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.open {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  align-self: flex-start;
  color: var(--accent);
  font-weight: 600;
}
.url {
  overflow-wrap: anywhere;
}
.actions {
  display: flex;
  gap: var(--space-2);
}
.result {
  margin: 0;
}
</style>
