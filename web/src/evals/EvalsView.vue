<script setup lang="ts">
import { Calculator, FlaskConical, Play } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, Banner, Card, Checkbox, ErrorBanner, Field, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop } from '../ui'
import { expectationTones, statusTones } from './tones'

/**
 * The evals admin view: eval cases (expectation, incomplete marker, note), the form for a new eval run (model per
 * tier, template, base prompt, judge, cost estimate) and the eval runs with their totals.
 */
type Tier = 'fast' | 'standard' | 'advisor'
const tiers: Tier[] = ['fast', 'standard', 'advisor']

const api = useApi()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const cases = useLoad(() => api.call((client) => client.GET('/api/v1/evals/cases')))
const runs = useLoad(() => api.call((client) => client.GET('/api/v1/evals/runs')))
const config = useLoad(() => api.call((client) => client.GET('/api/v1/config')), { isEmpty: () => false })
const forbidden = computed(() => cases.error.value?.kind === 'problem' && cases.error.value.status === 403)

const selected = shallowRef<string[]>([])
const draft = reactive({ models: { fast: '', standard: '', advisor: '' } as Record<Tier, string>, template: '', basePrompt: '', judge: '' })
const estimate = shallowRef<number | null>(null)
const allSelected = computed({
  get: () => !!cases.data.value?.length && selected.value.length === cases.data.value.length,
  set: (all: boolean) => (selected.value = all ? (cases.data.value ?? []).map((evalCase) => evalCase.id) : []),
})
watch([selected, draft], () => (estimate.value = null), { deep: true })

function request(): Schemas['EvalRunRequest'] {
  return {
    caseIds: selected.value,
    models: Object.fromEntries(Object.entries(draft.models).filter(([, key]) => key)),
    template: draft.template || null,
    basePrompt: draft.basePrompt.trim() || null,
    judgeModel: draft.judge || null,
  }
}

const estimating = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/evals/estimate', { body: request() }))
  if (result.data) estimate.value = result.data.estimatedCost
  return result
})

const start = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/evals/runs', { body: request() }))
  if (result.data) void router.push(`/system/evals/runs/${result.data.id}`)
  return result
})

const models = computed(() => config.data.value?.models ?? [])
const totals = (run: Schemas['EvalRunView']) => t('evals.runs.totals', { pass: run.pass, changed: run.changed, diverged: run.diverged, failed: run.failed })
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('evals.title')" :subtitle="desktop ? t('evals.subtitle') : undefined" back="/system" :back-label="t('shell.nav.system')" />

    <Banner v-if="forbidden" tone="warning" :title="t('evals.forbidden')" />
    <div v-else class="layout" :class="{ split: desktop }">
      <Card :title="t('evals.cases.title')" data-test="cases">
        <LoadBoundary :load="cases" :empty-title="t('evals.cases.title')" :empty-body="t('evals.cases.empty')" :empty-icon="FlaskConical">
          <div class="select">
            <Checkbox v-model="allSelected" :label="t('evals.cases.selectAll')" data-test="select-all" />
            <span class="t-body-s ink-3">{{ t('evals.cases.selected', { count: selected.length }) }}</span>
          </div>
          <ul class="cases">
            <li v-for="evalCase in cases.data.value" :key="evalCase.id" :data-case="evalCase.id" class="case">
              <Checkbox v-model="selected" :value="evalCase.id" :label="evalCase.input" :description="evalCase.note ?? t('evals.cases.noNote')" />
              <span class="badges">
                <Badge :tone="expectationTones[evalCase.expectation]" :label="t(`evals.expectation.${evalCase.expectation}`)" />
                <Badge v-if="evalCase.incomplete" tone="warning" :label="t('evals.incomplete')" :title="t('evals.incompleteHint')" data-test="incomplete" />
                <RouterLink :to="`/system/evals/cases/${evalCase.id}`" class="open t-body-s">{{ format.dateTime(evalCase.recordedAt) }}</RouterLink>
              </span>
            </li>
          </ul>
        </LoadBoundary>
      </Card>

      <div class="column">
        <Card :title="t('evals.form.title')" data-test="new-run">
          <form class="form" @submit.prevent="start.submit()">
            <Field v-for="tier in tiers" :key="tier" v-model="draft.models[tier]" kind="select" :name="`model-${tier}`" :label="t(`evals.form.tier.${tier}`)">
              <option value="">{{ t('evals.form.current') }}</option>
              <option v-for="model in models" :key="model.key" :value="model.key">{{ model.key }} · {{ model.model }}</option>
            </Field>
            <Field v-model="draft.template" kind="select" name="template" :label="t('evals.form.template')">
              <option value="">{{ t('evals.form.defaultTemplate') }}</option>
              <option v-for="template in (config.data.value?.templates ?? []).filter((item) => item.name !== 'default')" :key="template.name" :value="template.name">{{ template.name }}</option>
            </Field>
            <Field v-model="draft.basePrompt" kind="textarea" :rows="3" name="basePrompt" :label="t('evals.form.basePrompt')" :hint="t('evals.form.basePromptHint')" />
            <Field v-model="draft.judge" kind="select" name="judge" :label="t('evals.form.judge')" :hint="t('evals.form.judgeHint')">
              <option value="">{{ t('evals.form.noJudge') }}</option>
              <option v-for="model in models" :key="model.key" :value="model.key">{{ model.key }} · {{ model.model }}</option>
            </Field>
            <p v-if="estimate !== null" class="estimate num" data-test="estimate">{{ t('evals.form.estimated', { cost: format.cost(estimate) }) }}</p>
            <p v-else-if="!selected.length" class="t-body-s ink-3">{{ t('evals.form.noSelection') }}</p>
            <ErrorBanner v-if="estimating.error.value" :error="estimating.error.value" />
            <ErrorBanner v-if="start.error.value" :error="start.error.value" />
            <div class="actions">
              <AppButton :icon="Calculator" :disabled="!selected.length" :loading="estimating.busy.value" data-test="estimate-button" @click="estimating.submit()">{{ t('evals.form.estimate') }}</AppButton>
              <AppButton type="submit" variant="primary" :icon="Play" :disabled="!selected.length" :loading="start.busy.value" data-test="start">{{ t('evals.form.start') }}</AppButton>
            </div>
          </form>
        </Card>

        <Section :title="t('evals.runs.title')" data-test="runs">
          <LoadBoundary :load="runs" :empty-title="t('evals.runs.title')" :empty-body="t('evals.runs.empty')" :empty-icon="FlaskConical">
            <ListCard>
              <ListRow v-for="run in runs.data.value" :key="run.id" :to="`/system/evals/runs/${run.id}`" :data-run="run.id" :title="format.dateTime(run.createdAt)" :subtitle="`${totals(run)} · ${format.cost(run.cost)}`">
                <template #trail><Badge :tone="statusTones[run.status]" :label="t(`evals.status.${run.status}`)" /></template>
              </ListRow>
            </ListCard>
          </LoadBoundary>
        </Section>
      </div>
    </div>
  </Page>
</template>

<style scoped>
.layout,
.column,
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr);
  gap: var(--space-5);
  align-items: start;
}
.select {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
}
.cases {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0;
  list-style: none;
}
.case {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding: var(--space-3) 0;
  border-top: 1px solid var(--line);
  overflow-wrap: anywhere;
}
.badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  padding-left: 34px;
}
.open {
  margin-left: auto;
  color: var(--accent-text);
}
.estimate {
  font: 600 15px/20px var(--font-sans);
}
.actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>
