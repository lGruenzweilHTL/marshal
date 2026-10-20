<script setup lang="ts">
import { Square, Trash2 } from 'lucide-vue-next'
import { computed, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { AppButton, Badge, Card, ErrorBanner, KeyValueList, LoadBoundary, Page, PageHeader, useDesktop } from '../ui'
import { expectationTones, outcomeTones, statusTones } from './tones'

/**
 * The report of an eval run: totals per outcome and cost, then per case the recorded and replayed tool calls and
 * replies side by side with the case's note. Refreshes while the run is queued or running.
 */
const outcomes: Schemas['EvalOutcome'][] = ['pass', 'changed', 'diverged', 'failed']
const pollMs = 2000

const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)

const report = useLoad(() => api.call((client) => client.GET('/api/v1/evals/runs/{id}', { params: { path: { id } } })), { isEmpty: () => false })
const cases = useLoad(() => api.call((client) => client.GET('/api/v1/evals/cases')), { isEmpty: () => false })
const run = computed(() => report.data.value?.run ?? null)
const active = computed(() => run.value?.status === 'queued' || run.value?.status === 'running')
const caseById = computed(() => new Map((cases.data.value ?? []).map((evalCase) => [evalCase.id, evalCase])))

let timer: ReturnType<typeof setInterval> | null = null
watch(
  active,
  (running) => {
    if (running && !timer) timer = setInterval(() => report.refresh(), pollMs)
    if (!running && timer) {
      clearInterval(timer)
      timer = null
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => timer && clearInterval(timer))

const cancel = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/evals/runs/{id}/cancel', { params: { path: { id } } }))
  report.refresh()
  return result
})
const remove = useSubmit(async () => {
  const result = await api.call((client) => client.DELETE('/api/v1/evals/runs/{id}', { params: { path: { id } } }))
  if (!result.error) void router.push('/system/evals')
  return result
})

const count = (data: Schemas['EvalRunView'], outcome: Schemas['EvalOutcome']) => data[outcome]
const candidate = (data: Schemas['EvalRunView']) =>
  [
    ...Object.entries(data.candidate.models).map(([tier, key]) => `${t(`runs.tier.${tier}`)}: ${key}`),
    data.candidate.template && `${t('evals.form.template')}: ${data.candidate.template}`,
    data.candidate.basePrompt && t('evals.form.basePrompt'),
    data.candidate.judgeModel && `${t('evals.form.judge')}: ${data.candidate.judgeModel}`,
  ]
    .filter(Boolean)
    .join(' · ') || t('evals.form.current')
</script>

<template>
  <Page width="wide">
    <PageHeader :title="run ? `${t('evals.report.title')} · ${format.dateTime(run.createdAt)}` : t('evals.report.title')" back="/system/evals" :back-label="t('evals.title')">
      <template v-if="run" #subtitle><Badge :tone="statusTones[run.status]" :label="t(`evals.status.${run.status}`)" data-test="status" /></template>
      <template v-if="run" #actions>
        <AppButton v-if="active" :icon="Square" :loading="cancel.busy.value" data-test="cancel" @click="cancel.submit()">{{ t('evals.report.cancel') }}</AppButton>
        <AppButton v-else variant="danger" :icon="Trash2" :loading="remove.busy.value" data-test="delete" @click="remove.submit()">{{ t('evals.report.delete') }}</AppButton>
      </template>
    </PageHeader>

    <LoadBoundary :load="report" size="page">
      <template v-if="report.data.value && run">
        <ErrorBanner v-if="cancel.error.value" :error="cancel.error.value" />
        <ErrorBanner v-if="remove.error.value" :error="remove.error.value" />
        <div class="totals" data-test="totals">
          <Card v-for="outcome in outcomes" :key="outcome" class="total" :data-total="outcome">
            <span class="t-overline">{{ t(`evals.outcome.${outcome}`) }}</span>
            <span class="value num">{{ count(run, outcome) }}</span>
          </Card>
          <Card class="total" data-total="cost">
            <span class="t-overline">{{ t('evals.report.cost') }}</span>
            <span class="value num">{{ format.cost(run.cost) }}</span>
            <span class="t-body-s ink-3">{{ t('evals.report.estimated') }} {{ format.cost(run.estimatedCost) }}</span>
          </Card>
        </div>
        <Card>
          <KeyValueList
            :items="[
              { key: 'candidate', label: t('evals.report.candidate'), value: candidate(run) },
              { key: 'startedBy', label: t('evals.report.startedBy'), value: run.startedBy, mono: true },
              ...(run.error ? [{ key: 'error', label: t('evals.status.failed'), value: run.error }] : []),
            ]"
          />
        </Card>

        <Card v-for="result in report.data.value.results" :key="result.caseId" :data-result="result.caseId" class="result">
          <header class="result-head">
            <Badge :tone="outcomeTones[result.outcome]" :label="t(`evals.outcome.${result.outcome}`)" />
            <template v-if="caseById.get(result.caseId)">
              <Badge :tone="expectationTones[caseById.get(result.caseId)!.expectation]" :label="t(`evals.expectation.${caseById.get(result.caseId)!.expectation}`)" />
              <RouterLink :to="`/system/evals/cases/${result.caseId}`" class="input">{{ caseById.get(result.caseId)!.input }}</RouterLink>
            </template>
            <span v-else class="ink-3">{{ t('evals.report.deletedCase') }}</span>
            <span class="meta num">
              <template v-if="result.judgeScore !== null">{{ t('evals.report.judge', { score: result.judgeScore.toFixed(2) }) }} · </template>{{ format.cost(result.cost) }}
            </span>
          </header>
          <p v-if="caseById.get(result.caseId)?.note" class="note" data-test="note">{{ caseById.get(result.caseId)!.note }}</p>
          <p v-if="result.error" class="error">{{ result.error }}</p>
          <div class="sides" :class="{ split: desktop }">
            <section data-side="recorded">
              <span class="t-overline">{{ t('evals.report.recorded') }}</span>
              <ul class="calls">
                <li v-for="(call, index) in caseById.get(result.caseId)?.toolCalls ?? []" :key="index">
                  <span class="t-mono-s">{{ call.tool }}</span> <code class="t-mono-s args">{{ call.arguments }}</code>
                </li>
                <li v-if="!caseById.get(result.caseId)?.toolCalls?.length" class="ink-3">{{ t('evals.report.noCalls') }}</li>
              </ul>
              <p class="reply">{{ caseById.get(result.caseId)?.reply ?? t('evals.report.noReply') }}</p>
            </section>
            <section data-side="replayed">
              <span class="t-overline">{{ t('evals.report.replayed') }}</span>
              <ul class="calls">
                <li v-for="(call, index) in result.calls" :key="index" :class="{ unrecorded: !call.recorded }" :data-recorded="call.recorded">
                  <span class="t-mono-s">{{ call.tool }}</span> <code class="t-mono-s args">{{ call.arguments }}</code>
                  <Badge v-if="!call.recorded" tone="info" :label="t('evals.report.noRecording')" />
                </li>
                <li v-if="!result.calls.length" class="ink-3">{{ t('evals.report.noCalls') }}</li>
              </ul>
              <p class="reply">{{ result.reply ?? t('evals.report.noReply') }}</p>
            </section>
          </div>
        </Card>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.totals {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: var(--space-3);
}
.total {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.value {
  font: 650 28px/34px var(--font-display, var(--font-sans));
}
.result {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.result-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.input {
  color: var(--ink);
  font-weight: 600;
  overflow-wrap: anywhere;
}
.meta {
  margin-left: auto;
  color: var(--ink-3);
  font: var(--text-body-s);
}
.note {
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--ink-2);
  font: var(--text-body-s);
}
.error {
  color: var(--danger);
  font: var(--text-body-s);
}
.sides {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.sides.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
}
.sides section {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-width: 0;
}
.calls {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: 0;
  padding: 0;
  list-style: none;
}
.calls li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.args {
  color: var(--ink-2);
  overflow-wrap: anywhere;
}
.unrecorded .args {
  color: var(--info);
}
.reply {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
