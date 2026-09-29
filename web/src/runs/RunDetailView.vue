<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { formatCost, formatDateTime, snakeCase } from '../format'

const session = useApi()
const route = useRoute()
const detail = ref<Schemas['RunDetail'] | null>(null)
const error = ref<string | null>(null)

const kindLabels: Record<Schemas['RunStepKind'], string> = {
  context: 'Context build',
  modelCall: 'Model call',
  toolCall: 'Tool call',
  reply: 'Reply',
}

const noteHeadings: Record<string, string> = { project: 'Active projects', memory: 'Memory in context' }

/** Context-build notes grouped by kind: projects first, then memory, then anything else in order of appearance. */
function noteGroups(step: Schemas['RunStepDetail']): { kind: string; notes: Schemas['ContextNote'][] }[] {
  const kinds = [...new Set(step.notes.map((note) => note.kind))].sort((a, b) => rank(a) - rank(b))
  return kinds.map((kind) => ({ kind, notes: step.notes.filter((note) => note.kind === kind) }))
}

function rank(kind: string): number {
  const index = Object.keys(noteHeadings).indexOf(kind)
  return index < 0 ? Number.MAX_SAFE_INTEGER : index
}

watch(
  () => route.params.id,
  async (id) => {
    if (typeof id !== 'string') return
    detail.value = null
    const { data, response } = await session.api.GET('/api/v1/runs/{id}', { params: { path: { id } } })
    if (data) detail.value = data
    error.value = data ? null : response.status === 404 ? 'Run not found.' : 'The run could not be loaded.'
  },
  { immediate: true },
)
</script>

<template>
  <section class="page">
    <p><RouterLink to="/runs">← Runs</RouterLink></p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <template v-if="detail">
      <h1>Run {{ formatDateTime(detail.run.startedAt) }}</h1>
      <dl class="facts">
        <dt>Source</dt>
        <dd>{{ detail.run.source }}<template v-if="detail.run.inputMode"> ({{ detail.run.inputMode }})</template></dd>
        <dt>Trigger</dt>
        <dd>{{ detail.run.trigger }}</dd>
        <template v-if="detail.run.satelliteId">
          <dt>Satellite</dt>
          <dd>{{ detail.run.satelliteId }}</dd>
        </template>
        <dt>Outcome</dt>
        <dd>
          {{ detail.run.outcome }}<template v-if="detail.run.outcomeReason"> – {{ detail.run.outcomeReason }}</template>
        </dd>
        <dt>Cost</dt>
        <dd>{{ formatCost(detail.run.cost) }}</dd>
      </dl>
      <div v-if="detail.taintSources.length" data-test="taint" class="taint">
        <strong>Tainted</strong> by untrusted content:
        <ul>
          <li v-for="source in detail.taintSources" :key="source.kind">{{ source.kind }} (from step {{ source.firstStep }})</li>
        </ul>
      </div>
      <ol class="steps">
        <li
          v-for="step in detail.steps"
          :key="step.sequence"
          :data-step="step.sequence"
          :class="['step', { subagent: step.depth > 0 }]"
          :style="step.depth > 0 ? { marginInlineStart: `${step.depth * 1.5}rem` } : undefined"
        >
          <h2>
            {{ step.sequence }}. {{ kindLabels[step.kind] }}<template v-if="step.tool">: {{ step.tool }}</template>
            <span v-if="step.depth > 0" class="depth">subagent (depth {{ step.depth }})</span>
          </h2>
          <dl v-if="step.kind === 'modelCall'" class="facts">
            <dt>Model</dt>
            <dd>{{ step.model }} ({{ step.tier }})</dd>
            <dt>Tokens</dt>
            <dd>{{ step.tokensIn ?? '–' }} in, {{ step.tokensOut ?? '–' }} out, {{ step.tokensCached ?? '–' }} cached</dd>
            <dt>Latency</dt>
            <dd>
              <template v-if="step.firstTokenMs !== null">first token {{ step.firstTokenMs }} ms, </template>total {{ step.durationMs }} ms
            </dd>
            <dt>Cost</dt>
            <dd>{{ step.cost === null ? '–' : formatCost(step.cost) }}</dd>
          </dl>
          <dl v-if="step.kind === 'toolCall'" class="facts">
            <template v-if="step.riskTier">
              <dt>Risk tier</dt>
              <dd>{{ step.riskTier }}</dd>
            </template>
            <template v-if="step.guardDecision">
              <dt>Guard decision</dt>
              <dd>{{ snakeCase(step.guardDecision) }}</dd>
            </template>
            <template v-if="step.reasons.length">
              <dt>Confirmation reasons</dt>
              <dd>
                <ul>
                  <li v-for="reason in step.reasons" :key="reason.code">{{ reason.sentence }}</li>
                </ul>
              </dd>
            </template>
            <template v-if="step.durationMs">
              <dt>Duration</dt>
              <dd>{{ step.durationMs }} ms</dd>
            </template>
          </dl>
          <div v-for="group in noteGroups(step)" :key="group.kind" :data-notes="group.kind">
            <h3>{{ noteHeadings[group.kind] ?? group.kind }}</h3>
            <ul>
              <li v-for="(note, index) in group.notes" :key="index">
                {{ note.label }}<template v-if="note.detail"> – {{ note.detail }}</template><template v-if="note.score != null"> ({{ note.score.toFixed(2) }})</template>
              </li>
            </ul>
          </div>
          <template v-if="step.arguments">
            <h3>Arguments</h3>
            <pre>{{ step.arguments }}</pre>
          </template>
          <template v-if="step.input">
            <h3>Input</h3>
            <pre>{{ step.input }}</pre>
          </template>
          <p v-if="step.reference" data-test="reference">Result stored as reference only: <code>{{ step.reference }}</code></p>
          <template v-else-if="step.content">
            <h3 v-if="step.kind === 'toolCall'">Result</h3>
            <pre data-test="content">{{ step.content }}</pre>
          </template>
          <p v-if="step.error" class="error">{{ step.error }}</p>
        </li>
      </ol>
    </template>
  </section>
</template>

<style scoped>
.steps {
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.step {
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  min-width: 0;
}

.step h2 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}

.step.subagent {
  border-style: dashed;
}

.depth {
  margin-inline-start: 0.5rem;
  font-size: 0.8rem;
  font-weight: normal;
  color: var(--muted);
}

.step h3 {
  margin: 0.5rem 0 0.25rem;
  font-size: 0.9rem;
}

pre {
  margin: 0;
  padding: 0.5rem;
  overflow-x: auto;
  background: var(--surface);
  border-radius: 6px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.taint {
  padding: 0.5rem 0.75rem;
  border-left: 4px solid var(--warning);
  background: var(--surface);
}

.taint ul {
  margin: 0.25rem 0 0;
}
</style>
