<script setup lang="ts">
import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { AppButton, Badge, Card, ErrorBanner } from '../ui'
import { lineDiff } from './lineDiff'

/**
 * MCP tools that are new or changed since their last approval: the previous and current definitions side by side,
 * differences marked, and the decision. Definitions are server text; they are shown as plain text only.
 */
type Approval = Schemas['McpToolApproval']

const emit = defineEmits<{ decided: [] }>()

const api = useApi()
const { t } = useI18n()

const pending = useLoad<Approval[]>(() => api.call((client) => client.GET('/api/v1/config/mcp-tools/pending')))
const deciding = shallowRef<string | null>(null)

const key = (approval: Approval) => `${approval.serverId}/${approval.toolName}`
const pretty = (definition: unknown) => (definition == null ? '' : JSON.stringify(definition, null, 2))
const diff = (approval: Approval) => lineDiff(pretty(approval.previous), pretty(approval.current))

const decide = useSubmit(async (approval: Approval, approve: boolean) => {
  deciding.value = key(approval)
  const result = await api.call((client) =>
    client.POST('/api/v1/config/mcp-tools/decisions', { body: { serverId: approval.serverId, toolName: approval.toolName, approve } }),
  )
  deciding.value = null
  if (!result.error) {
    pending.refresh()
    emit('decided')
  }
  return result
})
</script>

<template>
  <div v-if="pending.data.value?.length" class="approvals" data-test="tool-approvals">
    <h3 class="t-title-3">{{ t('system.config.approvals.title', { count: pending.data.value.length }, pending.data.value.length) }}</h3>
    <p class="t-body-s ink-2">{{ t('system.config.approvals.hint') }}</p>
    <ErrorBanner v-if="decide.error.value" :error="decide.error.value" />
    <Card v-for="approval in pending.data.value" :key="key(approval)" class="approval" :data-approval="key(approval)">
      <div class="head">
        <span class="t-mono-s strong">{{ approval.serverId }} · {{ approval.toolName }}</span>
        <Badge :tone="approval.change === 'new' ? 'info' : 'warning'" :label="t(`system.config.approvals.change.${approval.change}`)" />
      </div>
      <div v-if="approval.previous" class="sides">
        <figure v-for="side in (['before', 'after'] as const)" :key="side" class="side" :data-side="side">
          <figcaption class="t-label ink-3">{{ t(`system.config.approvals.${side}`) }}</figcaption>
          <pre class="t-mono-s"><span v-for="(line, index) in diff(approval)[side]" :key="index" :class="{ changed: line.changed, [side]: line.changed }">{{ line.text }}
</span></pre>
        </figure>
      </div>
      <figure v-else class="side" data-side="after">
        <figcaption class="t-label ink-3">{{ t('system.config.approvals.definition') }}</figcaption>
        <pre class="t-mono-s">{{ pretty(approval.current) }}</pre>
      </figure>
      <div class="actions">
        <AppButton variant="primary" size="sm" :label="t('system.config.approvals.approve')" :loading="decide.busy.value && deciding === key(approval)" data-test="approve" @click="decide.submit(approval, true)" />
        <AppButton variant="secondary" size="sm" :label="t('system.config.approvals.reject')" :disabled="decide.busy.value" data-test="reject" @click="decide.submit(approval, false)" />
      </div>
    </Card>
  </div>
</template>

<style scoped>
.approvals {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin-bottom: var(--space-4);
}
.approval {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}
.strong {
  font-weight: 600;
}
.sides {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: var(--space-3);
}
.side {
  min-width: 0;
  margin: 0;
}
pre {
  margin: var(--space-1) 0 0;
  padding: var(--space-2);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  overflow-x: auto;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.changed {
  display: block;
}
.changed.before {
  background: var(--danger-soft);
  text-decoration: line-through;
}
.changed.after {
  background: var(--warning-soft);
}
.actions {
  display: flex;
  gap: var(--space-2);
}
</style>
