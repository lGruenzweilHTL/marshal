<script setup lang="ts">
import { Bot, Cpu, FlaskConical, Layers, MessageSquareText, ShieldAlert, Wrench } from 'lucide-vue-next'
import { computed, nextTick, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useFormat } from '../i18n/locale'
import { useUserSession } from '../session'
import { AppButton, AppIcon, Badge, Banner, Card, CodeBlock, ErrorBanner, KeyValueList, LoadBoundary, Page, PageHeader, useDesktop, type Tone } from '../ui'
import ReasoningBlock from '../chat/ReasoningBlock.vue'
import RatingButtons from './RatingButtons.vue'
import TaintFlag from './TaintFlag.vue'
import VoiceTimeline from './VoiceTimeline.vue'

type Step = Schemas['RunStepDetail']

/**
 * One run as a timeline of its steps: context build (with the projects and memory it used), model calls with
 * tokens, latency and their reasoning (collapsed), tool calls with risk tier and guard decision, and the reply. `#step-<n>` scrolls to a step
 * (Inbox links to the step that tainted a run). The owner rates the run; admins can save it as an eval case.
 */
const api = useApi()
const route = useRoute()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()
const id = String(route.params.id)
const user = useUserSession()
const isAdmin = computed(() => user.current.value?.roles.includes('admin') === true)

const detail = useLoad(() => api.call((client) => client.GET('/api/v1/runs/{id}', { params: { path: { id } } })), { isEmpty: () => false })
const run = computed(() => detail.data.value?.run ?? null)
const outcomeTone: Record<Schemas['RunOutcome'], Tone> = { ok: 'success', error: 'danger', backgrounded: 'warning', cancelled: 'neutral' }
/** Model calls whose reasoning the user opened, by step sequence. */
const openReasoning = reactive<Record<number, boolean>>({})
/** The eval case of the run: from the detail, or created here by a rating or "save as eval case". */
const createdCase = shallowRef<string | null>(null)
const caseId = computed(() => createdCase.value ?? detail.data.value?.evalCaseId ?? null)
const saveCase = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/runs/{id}/eval-case', { params: { path: { id } } }))
  if (result.data) createdCase.value = result.data.id
  return result
})
const taintedFrom = computed(() => Math.min(...(detail.data.value?.taintSources.map((source) => source.firstStep) ?? [])))

watch(
  () => detail.state.value,
  async (state) => {
    if (state !== 'ready' || !route.hash) return
    await nextTick()
    document.getElementById(route.hash.slice(1))?.scrollIntoView?.()
  },
)

const noteOrder = ['tool_namespace', 'project', 'memory']
function noteGroups(step: Step): { kind: string; notes: Schemas['ContextNote'][] }[] {
  const rank = (kind: string) => (noteOrder.includes(kind) ? noteOrder.indexOf(kind) : Number.MAX_SAFE_INTEGER)
  const kinds = [...new Set(step.notes.map((note) => note.kind))].sort((a, b) => rank(a) - rank(b))
  return kinds.map((kind) => ({ kind, notes: step.notes.filter((note) => note.kind === kind) }))
}

function look(step: Step): { icon: typeof Cpu; tone: string } {
  if (step.kind === 'context') return { icon: Layers, tone: 'ink-3' }
  if (step.kind === 'modelCall') return { icon: step.depth > 0 ? Bot : Cpu, tone: 'info' }
  if (step.kind === 'reply') return { icon: MessageSquareText, tone: 'success' }
  if (step.guardDecision === 'escalatedByTaint' || step.sequence === taintedFrom.value) return { icon: ShieldAlert, tone: 'warning' }
  if (step.guardDecision === 'denied' || step.error) return { icon: Wrench, tone: 'danger' }
  return { icon: Wrench, tone: 'ink-2' }
}

function meta(step: Step): string {
  const parts: string[] = []
  if (step.tier) parts.push(step.tier)
  if (step.firstTokenMs !== null) parts.push(t('runs.detail.firstToken', { ms: step.firstTokenMs }))
  if (step.durationMs) parts.push(t('runs.detail.total', { ms: step.durationMs }))
  if (step.cost !== null) parts.push(format.value.cost(step.cost))
  return parts.join(' · ')
}
const count = (value: number | null) => (value === null ? '–' : format.value.number(value))
</script>

<template>
  <Page width="wide">
    <PageHeader v-if="!run" :title="t('runs.detail.title')" back="/runs" :back-label="t('runs.title')" />
    <LoadBoundary :load="detail" size="page">
      <template v-if="detail.data.value && run">
        <PageHeader :title="`${t(`runs.source.${run.source}`)} · ${format.time(run.startedAt)}`" back="/runs" :back-label="t('runs.title')">
          <template #subtitle>
            <span class="headline">
              <Badge :tone="outcomeTone[run.outcome]" :label="t(`runs.outcome.${run.outcome}`)" />
              <TaintFlag v-if="run.tainted" />
              <span class="num">{{ format.dateTime(run.startedAt) }} · {{ format.cost(run.cost) }}</span>
            </span>
          </template>
        </PageHeader>

        <Banner v-if="detail.data.value.taintSources.length" tone="warning" :title="t('runs.detail.taintedTitle')" data-test="taint">
          {{ detail.data.value.taintSources.map((source) => t('runs.detail.taintSource', { kind: source.kind, step: source.firstStep })).join(', ') }}.
          {{ t('runs.detail.taintedBody') }}
        </Banner>

        <Card v-if="run.inputMode === 'voice' && run.endedAt">
          <VoiceTimeline :steps="detail.data.value.steps" :total-ms="Date.parse(run.endedAt) - Date.parse(run.startedAt)" :first-delta-ms="run.timeToFirstDeltaMs ?? null" />
        </Card>

        <div class="layout" :class="{ split: desktop }">
          <Card>
            <ol class="steps">
              <li
                v-for="(step, index) in detail.data.value.steps"
                :id="`step-${step.sequence}`"
                :key="step.sequence"
                :data-step="step.sequence"
                class="step"
                :class="{ subagent: step.depth > 0, last: index === detail.data.value.steps.length - 1 }"
                :style="step.depth > 0 ? { marginInlineStart: `calc(${step.depth} * var(--space-6))` } : undefined"
              >
                <span class="dot" :class="look(step).tone"><AppIcon :icon="look(step).icon" :size="18" /></span>
                <div class="body">
                  <div class="head">
                    <span class="num seq">{{ String(step.sequence).padStart(2, '0') }}</span>
                    <span class="title">{{ step.tool ?? t(`runs.detail.kind.${step.kind}`) }}</span>
                    <Badge v-if="step.depth > 0" :label="t('runs.detail.subagent', { depth: step.depth })" />
                    <span class="meta">{{ meta(step) }}</span>
                  </div>

                  <p v-if="step.kind === 'modelCall' && step.model" class="line">
                    <span class="t-mono-s">{{ step.model }}</span>
                    <span class="num ink-3">{{ t('runs.detail.tokens', { in: count(step.tokensIn), out: count(step.tokensOut), cached: count(step.tokensCached) }) }}</span>
                  </p>
                  <ReasoningBlock
                    v-if="step.reasoning || step.reasoningWithheld"
                    v-model:open="openReasoning[step.sequence]"
                    :text="step.reasoning ?? ''"
                    :withheld="step.reasoningWithheld"
                  />

                  <p v-if="step.riskTier || step.guardDecision" class="line">
                    <Badge v-if="step.riskTier" :label="t(`runs.detail.risk.${step.riskTier}`)" :tone="step.riskTier === 'safe' ? 'neutral' : 'warning'" />
                    <Badge
                      v-if="step.guardDecision"
                      :label="t(`runs.detail.guard.${step.guardDecision}`)"
                      :tone="step.guardDecision === 'denied' || step.guardDecision === 'judgeNegative' ? 'danger' : step.guardDecision === 'escalatedByTaint' ? 'warning' : 'neutral'"
                    />
                  </p>
                  <ul v-if="step.reasons.length" class="reasons">
                    <li v-for="reason in step.reasons" :key="reason.code">{{ reason.sentence }}</li>
                  </ul>

                  <div v-for="group in noteGroups(step)" :key="group.kind" :data-notes="group.kind" class="notes">
                    <span class="t-overline">{{ noteOrder.includes(group.kind) ? t(`runs.detail.notes.${group.kind}`) : group.kind }}</span>
                    <ul>
                      <li v-for="(note, noteIndex) in group.notes" :key="noteIndex">
                        {{ note.label }}<template v-if="note.detail"> – {{ note.detail }}</template><template v-if="note.score != null"> ({{ note.score.toFixed(2) }})</template>
                      </li>
                    </ul>
                  </div>

                  <CodeBlock v-if="step.arguments" :code="step.arguments" :label="t('runs.detail.arguments')" />
                  <CodeBlock v-if="step.input" :code="step.input" :label="t('runs.detail.input')" />
                  <p v-if="step.reference" data-test="reference" class="ink-2">
                    {{ t('runs.detail.reference') }} <code class="t-mono-s">{{ step.reference }}</code>
                  </p>
                  <template v-else-if="step.content">
                    <p v-if="step.kind === 'reply' || step.kind === 'context'" data-test="content" class="content">{{ step.content }}</p>
                    <CodeBlock v-else data-test="content" :code="step.content" :label="t('runs.detail.result')" />
                  </template>
                  <p v-if="step.error" class="error">{{ step.error }}</p>
                </div>
              </li>
            </ol>
          </Card>

          <Card class="facts">
            <KeyValueList
              :items="[
                { key: 'source', label: t('runs.detail.facts.source'), value: [t(`runs.source.${run.source}`), run.inputMode].filter(Boolean).join(' · ') },
                { key: 'trigger', label: t('runs.detail.facts.trigger'), value: run.trigger },
                ...(run.satelliteId ? [{ key: 'satellite', label: t('runs.detail.facts.satellite'), value: run.satelliteId }] : []),
                { key: 'started', label: t('runs.detail.facts.started'), value: format.dateTime(run.startedAt) },
                ...(run.endedAt ? [{ key: 'duration', label: t('runs.detail.facts.duration'), value: format.duration(Date.parse(run.endedAt) - Date.parse(run.startedAt)) }] : []),
                ...(run.timeToFirstDeltaMs != null ? [{ key: 'firstDelta', label: t('runs.detail.facts.firstDelta'), value: format.duration(run.timeToFirstDeltaMs) }] : []),
                { key: 'outcome', label: t('runs.detail.facts.outcome') },
                { key: 'cost', label: t('runs.detail.facts.cost'), value: format.cost(run.cost) },
                ...(detail.data.value.language ? [{ key: 'language', label: t('runs.detail.facts.language'), value: detail.data.value.language }] : []),
              ]"
            >
              <template #outcome>
                <Badge :tone="outcomeTone[run.outcome]" :label="t(`runs.outcome.${run.outcome}`)" />
                <span v-if="run.outcomeReason" class="ink-3"> {{ run.outcomeReason }}</span>
              </template>
            </KeyValueList>
            <section class="rate" data-test="run-rating">
              <span class="t-overline">{{ t('runs.rating.title') }}</span>
              <RatingButtons :run-id="run.id" :rating="run.rating" :note="run.ratingNote" @rated="(_rating, _note, created) => created && (createdCase = created)" />
              <p class="t-body-s ink-3">{{ t('runs.rating.hint') }}</p>
              <template v-if="isAdmin">
                <AppButton v-if="caseId" size="sm" :icon="FlaskConical" :to="`/system/evals/cases/${caseId}`" data-test="open-case">{{ t('runs.rating.openCase') }}</AppButton>
                <AppButton v-else size="sm" :icon="FlaskConical" :loading="saveCase.busy.value" data-test="save-case" @click="saveCase.submit()">{{ t('runs.rating.saveCase') }}</AppButton>
                <ErrorBanner v-if="saveCase.error.value" :error="saveCase.error.value" />
              </template>
            </section>
          </Card>
        </div>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.rate {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-2);
  margin-top: var(--space-4);
  padding-top: var(--space-4);
  border-top: 1px solid var(--line);
}
.headline {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}
.layout {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 340px;
  gap: 28px;
  align-items: start;
}
.split .facts {
  position: sticky;
  top: var(--space-8);
}
.layout:not(.split) .facts {
  order: -1;
}
.steps {
  margin: 0;
  padding: 0;
  list-style: none;
}
.step {
  position: relative;
  display: flex;
  gap: 14px;
  padding-bottom: 14px;
  scroll-margin-top: var(--space-8);
}
.step:not(.last)::before {
  content: '';
  position: absolute;
  top: 40px;
  bottom: -2px;
  left: 17px;
  width: 2px;
  background: var(--line);
}
.dot {
  z-index: 1;
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: 1px solid var(--line);
  border-radius: var(--radius-full);
  background: var(--surface);
}
.dot.ink-2 {
  color: var(--ink-2);
}
.dot.info {
  color: var(--info);
}
.dot.success {
  color: var(--success);
}
.dot.warning {
  color: var(--warning);
}
.dot.danger {
  color: var(--danger);
}
.body {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  gap: var(--space-2);
  min-width: 0;
  padding-top: 6px;
}
.head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.seq {
  color: var(--ink-3);
  font: var(--text-mono-s);
}
.title {
  font: 600 15px/20px var(--font-sans);
  overflow-wrap: anywhere;
}
.meta {
  margin-left: auto;
  color: var(--ink-3);
  font: var(--text-body-s);
}
.line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  font: var(--text-body-s);
}
.reasons,
.notes ul {
  margin: 0;
  padding-left: var(--space-5);
  color: var(--ink-2);
  font: var(--text-body-s);
}
.notes {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.content {
  color: var(--ink-2);
  white-space: pre-wrap;
}
.error {
  color: var(--danger);
  font: var(--text-body-s);
}
</style>
