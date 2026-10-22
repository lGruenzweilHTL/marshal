<script setup lang="ts">
import { Inbox, Zap } from 'lucide-vue-next'
import { computed, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { useInboxCountOptional } from '../layout/inboxCount'
import { AppButton, Chip, ErrorBanner, IconTile, ListCard, ListRow, LoadBoundary, LoadState, Page, PageHeader, Section, Segmented, useDesktop, useNow, useToasts } from '../ui'
import ApprovalTicket, { type TicketResult } from './ApprovalTicket.vue'
import ApprovalContext from './ApprovalContext.vue'
import InputRequestForm from './InputRequestForm.vue'
import NotificationRow from './NotificationRow.vue'
import ProposalRow from './ProposalRow.vue'
import SkillProposalRow from './SkillProposalRow.vue'
import SuggestionCard from './SuggestionCard.vue'

type Filter = 'all' | 'approvals' | 'changes' | 'alerts'
type Outcome = Schemas['NotificationOutcome']
type Answer = Schemas['SuggestionAnswerItem']

const api = useApi()
const inbox = useInboxCountOptional()
const toasts = useToasts()
const desktop = useDesktop()
const now = useNow()
const { t } = useI18n()

// One load per list, so one failing list keeps the others on screen.
const approvals = useLoad(() => api.call((client) => client.GET('/api/v1/approvals')))
const inputs = useLoad(() => api.call((client) => client.GET('/api/v1/approvals/inputs')))
const proposals = useLoad(() => api.call((client) => client.GET('/api/v1/proposals')))
const skills = useLoad(() => api.call((client) => client.GET('/api/v1/skill-proposals')))
const suggestions = useLoad(() => api.call((client) => client.GET('/api/v1/suggestions')))
const pageSize = 50
const outcome = shallowRef<Outcome | null>(null)
const older = shallowRef<Schemas['NotificationEntry'][]>([])
const lastPage = shallowRef<number | null>(null)
const notifications = useLoad(() => {
  older.value = []
  lastPage.value = null
  return page()
})
const more = useSubmit(async () => {
  const items = [...(notifications.data.value ?? []), ...older.value]
  const result = await page(items.at(-1)?.createdAt)
  if (result.data) {
    older.value = [...older.value, ...result.data]
    lastPage.value = result.data.length
  }
  return result
})

function page(before?: string) {
  return api.call((client) =>
    client.GET('/api/v1/notifications', { params: { query: { outcome: outcome.value ?? undefined, before, limit: pageSize } } }),
  )
}

const filter = shallowRef<Filter>('all')
const selected = shallowRef<string | null>(null)

const filters = computed(() => {
  const count = (items: readonly unknown[] | null) => (items?.length ? ` ${items.length}` : '')
  return [
    { value: 'all' as const, label: t('inbox.filters.all') },
    { value: 'approvals' as const, label: t('inbox.filters.approvals') + count([...(approvals.data.value ?? []), ...(inputs.data.value ?? [])]) },
    { value: 'changes' as const, label: t('inbox.filters.changes') + count([...(proposals.data.value ?? []), ...(skills.data.value ?? [])]) },
    { value: 'alerts' as const, label: t('inbox.filters.alerts') + count([...(suggestions.data.value ?? []), ...(notifications.data.value ?? [])]) },
  ]
})
const show = (kind: Exclude<Filter, 'all'>) => filter.value === 'all' || filter.value === kind
const allEmpty = computed(() => !outcome.value && [approvals, inputs, proposals, skills, suggestions, notifications].every((load) => load.state.value === 'empty'))

const outcomes: Outcome[] = ['pending', 'delivered', 'coalesced', 'held', 'inboxed', 'expired', 'fallback']
const shownNotifications = computed(() => [...(notifications.data.value ?? []), ...older.value])
// A full page means there may be older ones; the server has no total count.
const hasMore = computed(() => (lastPage.value ?? notifications.data.value?.length) === pageSize)

const current = computed(() => {
  const items = approvals.data.value ?? []
  return items.find((item) => item.id === selected.value) ?? items[0] ?? null
})

function left(approval: Schemas['ApprovalItem']) {
  const seconds = Math.max(0, Math.ceil((Date.parse(approval.expiresAt) - now.value) / 1000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')} ${t('inbox.ticket.left')}`
}

function toggleOutcome(value: Outcome) {
  outcome.value = outcome.value === value ? null : value
  notifications.retry()
}

function decided(result: TicketResult) {
  toasts.show(t(result === 'approved' ? 'inbox.ticket.approved' : result === 'rejected' ? 'inbox.ticket.rejected' : 'inbox.ticket.expiredResult'))
  approvals.refresh()
}

function answered(accepted: boolean, answer: Answer) {
  const result = answer.actionResult ? ` ${answer.actionResult}` : ''
  toasts.show(accepted ? t('inbox.suggestion.accepted') + result : t('inbox.suggestion.dismissed'))
  suggestions.refresh()
}

onMounted(() => inbox?.markSeen())
</script>

<template>
  <Page :width="desktop ? 'wide' : 'default'">
    <PageHeader :title="t('inbox.title')" :subtitle="t('inbox.subtitle')" />
    <LoadState v-if="allEmpty" state="empty" size="page" :icon="Inbox" :title="t('inbox.empty.title')" :body="t('inbox.empty.body')" />
    <div v-else class="layout" :class="{ split: desktop }">
      <div class="list">
        <Segmented v-model="filter" :options="filters" :label="t('inbox.title')" block data-test="filter" />

        <Section v-if="show('approvals')" :title="t('inbox.approvals')" :count="approvals.data.value?.length" data-section="approvals">
          <LoadBoundary :load="approvals" :empty-title="t('inbox.emptyFilter')">
            <template #default="{ data }">
              <ListCard v-if="desktop">
                <ListRow
                  v-for="approval in data"
                  :key="approval.id"
                  class="select"
                  :class="{ active: approval.id === current?.id }"
                  :title="approval.summary"
                  :chevron="false"
                  role="button"
                  tabindex="0"
                  :aria-pressed="approval.id === current?.id"
                  :data-select="approval.id"
                  @click="selected = approval.id"
                  @keydown.enter.space.prevent="selected = approval.id"
                >
                  <template #lead><IconTile :icon="Zap" tone="accent" /></template>
                  <template #subtitle><span class="t-mono-s">{{ approval.tool }}</span> · <span class="num">{{ left(approval) }}</span></template>
                </ListRow>
              </ListCard>
              <div v-else class="tickets">
                <ApprovalTicket v-for="approval in data" :key="approval.id" :approval="approval" density="compact" @decided="decided" />
              </div>
            </template>
          </LoadBoundary>
        </Section>

        <Section v-if="show('approvals') && inputs.data.value?.length" :title="t('inbox.inputs')" :count="inputs.data.value.length" data-section="inputs">
          <div class="tickets">
            <InputRequestForm v-for="request in inputs.data.value" :key="request.id" :request="request" density="compact" @settled="inputs.refresh()" />
          </div>
        </Section>

        <Section v-if="show('changes')" :title="t('inbox.proposals')" :count="proposals.data.value?.length" data-section="proposals">
          <LoadBoundary :load="proposals" :empty-title="t('inbox.emptyFilter')">
            <template #default="{ data }">
              <ListCard><ProposalRow v-for="item in data" :key="item.id" :proposal="item" /></ListCard>
            </template>
          </LoadBoundary>
        </Section>

        <Section v-if="show('changes') && skills.state.value !== 'empty'" :title="t('inbox.skills')" :count="skills.data.value?.length" data-section="skills">
          <LoadBoundary :load="skills">
            <template #default="{ data }">
              <ListCard><SkillProposalRow v-for="item in data" :key="item.id" :proposal="item" /></ListCard>
            </template>
          </LoadBoundary>
        </Section>

        <Section v-if="show('alerts') && suggestions.state.value !== 'empty'" :title="t('inbox.suggestions')" :count="suggestions.data.value?.length" data-section="suggestions">
          <LoadBoundary :load="suggestions">
            <template #default="{ data }">
              <div class="tickets"><SuggestionCard v-for="item in data" :key="item.id" :suggestion="item" @answered="answered" /></div>
            </template>
          </LoadBoundary>
        </Section>

        <Section v-if="show('alerts')" :title="t('inbox.notifications')" :count="notifications.data.value?.length" data-section="notifications">
          <template #actions>
            <div v-if="filter === 'alerts'" class="outcomes" role="group" :aria-label="t('inbox.outcomeFilter')">
              <Chip
                v-for="value in outcomes"
                :key="value"
                :label="t(`inbox.notification.outcome.${value}`)"
                :model-value="outcome === value"
                :data-outcome="value"
                @update:model-value="toggleOutcome(value)"
              />
            </div>
          </template>
          <LoadBoundary :load="notifications" :empty-title="t('inbox.emptyFilter')">
            <ListCard>
              <NotificationRow v-for="item in shownNotifications" :key="item.id" :notification="item" />
              <template v-if="hasMore" #footer>
                <AppButton variant="ghost" block data-test="more" :loading="more.busy.value" @click="more.submit()">{{ t('inbox.more') }}</AppButton>
              </template>
            </ListCard>
            <ErrorBanner v-if="more.error.value" :error="more.error.value" />
          </LoadBoundary>
        </Section>
      </div>

      <div v-if="desktop && current" class="detail" data-test="detail">
        <ApprovalTicket :key="current.id" :approval="current" @decided="decided" />
        <ApprovalContext :approval="current" />
      </div>
    </div>
  </Page>
</template>

<style scoped>
.layout,
.list {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.split {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr);
  gap: 28px;
  align-items: start;
}
.detail {
  position: sticky;
  top: var(--space-8);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.tickets {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.select {
  cursor: pointer;
}
.select.active {
  background: var(--accent-soft);
}
.outcomes {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>
