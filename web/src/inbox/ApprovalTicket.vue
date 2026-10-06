<script setup lang="ts">
import { Check, Clock, Info, ShieldAlert, X, Zap } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, AppIcon, CodeBlock, ErrorBanner, useNow } from '../ui'
import { useInboxCountOptional } from '../layout/inboxCount'

export type TicketResult = 'approved' | 'rejected' | 'expired'

/**
 * A background approval as a ticket: tool, summary, countdown to expiry, frozen arguments, the reasons and the taint
 * sources, with approve / reject. The decision goes to the API here, so every place that shows an approval behaves
 * the same; the parent hears `decided` once the server accepted it.
 */
const props = withDefaults(defineProps<{ approval: Schemas['ApprovalItem']; density?: 'full' | 'compact' }>(), { density: 'full' })
const emit = defineEmits<{ decided: [result: TicketResult] }>()

const api = useApi()
const inbox = useInboxCountOptional()
const { t } = useI18n()
const now = useNow()
const result = shallowRef<TicketResult | null>(null)
const pressed = shallowRef<'approve' | 'reject' | null>(null)

const remaining = computed(() => Math.max(0, Date.parse(props.approval.expiresAt) - now.value))
const total = computed(() => Math.max(1, Date.parse(props.approval.expiresAt) - Date.parse(props.approval.createdAt)))
const fraction = computed(() => remaining.value / total.value)
const clock = computed(() => {
  const seconds = Math.ceil(remaining.value / 1000)
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
})
const expired = computed(() => remaining.value <= 0)
const state = computed<TicketResult | 'pending'>(() => result.value ?? (expired.value ? 'expired' : 'pending'))
const circumference = 94.25
const args = computed(() => JSON.stringify(props.approval.arguments, null, 2))
const why = computed(() => props.approval.reasons.filter((reason) => reason.code !== 'taint').map((reason) => reason.sentence).join(' '))

const decision = useSubmit((approve: boolean) =>
  api.call((client) =>
    client.POST(approve ? '/api/v1/approvals/{id}/approve' : '/api/v1/approvals/{id}/reject', { params: { path: { id: props.approval.id } } }),
  ),
)

async function decide(approve: boolean) {
  pressed.value = approve ? 'approve' : 'reject'
  const outcome = await decision.submit(approve)
  if (outcome.error) {
    // Decided elsewhere or expired meanwhile: show the outcome instead of an action that can no longer work.
    if (outcome.error.kind === 'problem' && outcome.error.code === 'approval.expired') result.value = 'expired'
    return
  }
  result.value = approve ? 'approved' : 'rejected'
  void inbox?.refresh()
  emit('decided', result.value)
}
</script>

<template>
  <article class="ticket" :class="[density, state]" :aria-label="t('inbox.ticket.label')" :data-approval="approval.id">
    <div class="top">
      <div class="meta">
        <span class="tool"><AppIcon :icon="Zap" :size="14" :stroke="2" />{{ approval.tool }}</span>
        <span v-if="state === 'pending'" class="ttl" :class="{ low: fraction < 0.25 }">
          <span class="ttl-text"><span class="num ttl-clock" data-test="ttl">{{ clock }}</span><span class="ttl-label">{{ t('inbox.ticket.left') }}</span></span>
          <svg width="34" height="34" viewBox="0 0 36 36" aria-hidden="true">
            <circle class="ring-track" cx="18" cy="18" r="15" />
            <circle class="ring" cx="18" cy="18" r="15" :stroke-dasharray="`${(circumference * fraction).toFixed(1)} ${circumference}`" transform="rotate(-90 18 18)" />
          </svg>
        </span>
      </div>
      <h3 class="summary">{{ approval.summary }}</h3>
      <p v-if="approval.taint.length" class="taint" data-test="taint">
        <AppIcon :icon="ShieldAlert" :size="16" />
        <span>
          {{ t('inbox.ticket.taint') }}
          <template v-for="(source, index) in approval.taint" :key="source.kind + source.firstStep">
            <template v-if="index > 0">, </template>
            <RouterLink v-if="approval.runId" :to="`/runs/${approval.runId}#step-${source.firstStep}`">{{ t('inbox.ticket.taintStep', { kind: source.kind, step: source.firstStep }) }}</RouterLink>
            <template v-else>{{ t('inbox.ticket.taintStep', { kind: source.kind, step: source.firstStep }) }}</template>
          </template>
        </span>
      </p>
    </div>
    <div class="perforation" aria-hidden="true"><span class="line" /><span class="notch left" /><span class="notch right" /></div>
    <div class="bottom">
      <template v-if="density === 'full'">
        <div class="args">
          <span class="t-overline">{{ t('inbox.ticket.args') }}</span>
          <CodeBlock :code="args" />
        </div>
        <p v-if="why" class="why">
          <AppIcon :icon="Info" :size="18" class="why-icon" />
          <span><strong>{{ t('inbox.ticket.why') }}:</strong> <span data-test="reason">{{ why }}</span></span>
        </p>
      </template>
      <ErrorBanner v-if="decision.error.value" :error="decision.error.value" />
      <div v-if="state === 'pending'" class="actions">
        <AppButton
          :size="density === 'full' ? 'lg' : 'md'"
          :icon="X"
          block
          data-test="reject"
          :loading="decision.busy.value && pressed === 'reject'"
          :disabled="decision.busy.value"
          @click="decide(false)"
        >{{ t('actions.reject') }}</AppButton>
        <AppButton
          variant="primary"
          :size="density === 'full' ? 'lg' : 'md'"
          :icon="Check"
          block
          data-test="approve"
          :loading="decision.busy.value && pressed === 'approve'"
          :disabled="decision.busy.value"
          @click="decide(true)"
        >{{ t('actions.approve') }}</AppButton>
      </div>
      <div v-else class="result" role="status" data-test="result">
        <AppIcon :icon="state === 'approved' ? Check : state === 'rejected' ? X : Clock" :size="18" />
        <span>{{ state === 'approved' ? t('inbox.ticket.approved') : state === 'rejected' ? t('inbox.ticket.rejected') : t('inbox.ticket.expiredResult') }}</span>
      </div>
    </div>
  </article>
</template>

<style scoped>
.ticket {
  position: relative;
  width: 100%;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-2);
}
.top {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: 18px var(--space-5) 14px;
}
.compact .top {
  padding: 14px var(--space-4) 10px;
}
.meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}
.tool {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 28px;
  padding: 0 10px;
  border-radius: var(--radius-sm);
  background: var(--accent-soft);
  color: var(--accent-text);
  font: 500 13px/1 var(--font-mono);
  /* A narrow ticket moves the timer to the next line before the tool name breaks. */
  max-width: 100%;
  overflow-wrap: anywhere;
}
.ttl {
  display: flex;
  margin-left: auto;
  align-items: center;
  gap: var(--space-2);
  --ttl: var(--accent-text);
}
.ttl.low {
  --ttl: var(--warning);
}
.ttl-text {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}
.ttl-clock {
  font: 650 15px/18px var(--font-sans);
  color: var(--ttl);
}
.ttl-label {
  font: 400 11px/14px var(--font-sans);
  color: var(--ink-3);
}
.ring-track,
.ring {
  fill: none;
  stroke-width: 4;
}
.ring-track {
  stroke: var(--surface-3);
}
.ring {
  stroke: var(--ttl);
  stroke-linecap: round;
  transition: stroke-dasharray 1s linear;
}
.summary {
  font: 620 20px/26px var(--font-display);
  letter-spacing: -0.012em;
  text-wrap: pretty;
}
.compact .summary {
  font-size: 17px;
  line-height: 23px;
}
.taint {
  display: flex;
  gap: var(--space-2);
  align-items: flex-start;
  padding: var(--space-2) 10px;
  border-radius: var(--radius-sm);
  background: var(--warning-soft);
  color: var(--warning);
  font: 550 13px/18px var(--font-sans);
}
.taint a {
  color: inherit;
}
.perforation {
  position: relative;
  height: 20px;
}
.line {
  position: absolute;
  left: 22px;
  right: 22px;
  top: 9px;
  border-top: 2px dashed var(--line);
}
.notch {
  position: absolute;
  top: 0;
  width: 20px;
  height: 20px;
  border-radius: var(--radius-full);
  background: var(--bg);
  border: 1px solid var(--line);
}
.notch.left {
  left: -11px;
  clip-path: inset(0 0 0 50%);
}
.notch.right {
  right: -11px;
  clip-path: inset(0 50% 0 0);
}
.bottom {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: var(--space-2) var(--space-5) var(--space-5);
}
.compact .bottom {
  padding: var(--space-1) var(--space-4) var(--space-4);
}
.args {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.why {
  display: flex;
  gap: var(--space-2);
  align-items: flex-start;
  font: 400 14px/20px var(--font-sans);
  color: var(--ink-2);
}
.why strong {
  font-weight: 600;
  color: var(--ink);
}
.why-icon {
  color: var(--ink-3);
}
.actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
.result {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--target);
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  background: var(--surface-3);
  color: var(--ink-2);
  font: 600 14px/20px var(--font-sans);
}
.approved .result {
  background: var(--success-soft);
  color: var(--success);
}
</style>
