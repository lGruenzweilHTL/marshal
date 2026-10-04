<script setup lang="ts">
import { Bell, MessageSquare, Play } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import ApprovalTicket, { type TicketResult } from '../inbox/ApprovalTicket.vue'
import NotificationRow from '../inbox/NotificationRow.vue'
import { useLiveEvent } from '../live/liveEvents'
import PrinterCard from '../printers/PrinterCard.vue'
import { needsAttention, printerStatus } from '../printers/printerStatus'
import { usePrinters } from '../printers/usePrinters'
import RunRow from '../runs/RunRow.vue'
import KillSwitch from '../tasks/KillSwitch.vue'
import { AppButton, ListCard, LoadBoundary, Page, PageHeader, Section, useDesktop, useToasts } from '../ui'
import HealthStrip from './HealthStrip.vue'
import TaskList from './TaskList.vue'
import TodayCost from './TodayCost.vue'

type Block = 'approvals' | 'printer' | 'tasks' | 'notifications' | 'runs' | 'cost'

/**
 * Home, ordered by what needs the user: approvals, then printers (alerts first), running tasks with the kill switch,
 * the latest notifications, recent runs and today's cost. Every block loads and fails on its own.
 */
const api = useApi()
const desktop = useDesktop()
const toasts = useToasts()
const format = useFormat()
const { t } = useI18n()

const approvals = useLoad(() => api.call((client) => client.GET('/api/v1/approvals')))
const { load: printerLoad, printers } = usePrinters()
const tasks = useLoad(() => api.call((client) => client.GET('/api/v1/tasks', { params: { query: { status: 'active' } } })), { isEmpty: () => false })
const notifications = useLoad(() => api.call((client) => client.GET('/api/v1/notifications', { params: { query: { limit: 4 } } })))
const runs = useLoad(() => api.call((client) => client.GET('/api/v1/runs', { params: { query: { limit: 5 } } })))
const cost = useLoad(() => api.call((client) => client.GET('/api/v1/cost')), { isEmpty: () => false })

useLiveEvent('tasks', 'task.changed', () => tasks.refresh())
useLiveEvent('notifications', 'notification.created', () => notifications.refresh())
useLiveEvent('runs', 'run.completed', () => {
  runs.refresh()
  cost.refresh()
})

const sortedPrinters = computed(() => [...printers.value].sort((a, b) => Number(needsAttention(printerStatus(b))) - Number(needsAttention(printerStatus(a)))))
const running = computed(() => (tasks.data.value ?? []).filter((task) => task.status === 'running').length)
const today = computed(() => format.value.date(new Date()))

// Phones get one column in priority order; desktops put what needs a decision left and status right.
const columns = computed<Block[][]>(() =>
  desktop.value ? [['approvals', 'notifications', 'runs'], ['printer', 'tasks', 'cost']] : [['approvals', 'printer', 'tasks', 'notifications', 'runs', 'cost']],
)
const visible = (block: Block) =>
  block === 'approvals' ? approvals.state.value !== 'empty' : block === 'printer' ? printerLoad.state.value !== 'empty' : true

function decided(result: TicketResult) {
  toasts.show(t(result === 'approved' ? 'inbox.ticket.approved' : result === 'rejected' ? 'inbox.ticket.rejected' : 'inbox.ticket.expiredResult'))
  approvals.refresh()
}
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('shell.nav.home')" :subtitle="today">
      <template v-if="desktop" #actions>
        <AppButton :icon="MessageSquare" to="/chat">{{ t('home.newChat') }}</AppButton>
      </template>
    </PageHeader>
    <HealthStrip />
    <div class="columns" :class="{ split: desktop }">
      <div v-for="(column, index) in columns" :key="index" class="column">
        <template v-for="block in column" :key="block">
          <Section
            v-if="block === 'approvals' && visible(block)"
            :title="t('home.needsYou')"
            :count="approvals.data.value?.length"
            :action="t('shell.nav.inbox')"
            to="/inbox"
            data-section="approvals"
          >
            <LoadBoundary :load="approvals">
              <template #default="{ data }">
                <div class="tickets" :class="{ grid: desktop && data.length > 1 }">
                  <ApprovalTicket v-for="approval in data" :key="approval.id" :approval="approval" density="compact" @decided="decided" />
                </div>
              </template>
            </LoadBoundary>
          </Section>

          <Section v-else-if="block === 'printer' && visible(block)" :title="t('home.printer')" :action="t('shell.nav.printer')" to="/printers" data-section="printer">
            <LoadBoundary :load="printerLoad">
              <div class="printers">
                <PrinterCard v-for="printer in sortedPrinters" :key="printer.name" :printer="printer" :density="desktop ? 'full' : 'compact'" :actions="desktop" />
              </div>
            </LoadBoundary>
          </Section>

          <Section v-else-if="block === 'tasks'" :title="t('home.tasks')" :action="t('home.all')" to="/tasks" data-section="tasks">
            <LoadBoundary :load="tasks">
              <template #default="{ data }">
                <div class="stack">
                  <TaskList :tasks="data" />
                  <KillSwitch v-if="running > 0" :running="running" @changed="tasks.refresh()" />
                </div>
              </template>
            </LoadBoundary>
          </Section>

          <Section
            v-else-if="block === 'notifications'"
            :title="t('home.latest')"
            :action="t('home.allNotifications')"
            to="/inbox"
            data-section="notifications"
          >
            <LoadBoundary :load="notifications" :empty-title="t('home.noNotifications')" :empty-icon="Bell">
              <template #default="{ data }">
                <ListCard><NotificationRow v-for="item in data.slice(0, 4)" :key="item.id" :notification="item" /></ListCard>
              </template>
            </LoadBoundary>
          </Section>

          <Section v-else-if="block === 'runs'" :title="t('home.recentRuns')" :action="t('home.runs')" to="/runs" data-section="runs">
            <LoadBoundary :load="runs" :empty-title="t('home.noRuns')" :empty-icon="Play">
              <template #default="{ data }">
                <ListCard><RunRow v-for="run in data.slice(0, 5)" :key="run.id" :run="run" /></ListCard>
              </template>
            </LoadBoundary>
          </Section>

          <Section v-else-if="block === 'cost'" :title="t('home.cost')" :action="t('home.openCost')" to="/cost" data-section="cost">
            <LoadBoundary :load="cost">
              <template #default="{ data }"><TodayCost :report="data" /></template>
            </LoadBoundary>
          </Section>
        </template>
      </div>
    </div>
  </Page>
</template>

<style scoped>
.columns,
.column,
.printers,
.stack,
.tickets {
  display: flex;
  flex-direction: column;
}
.columns,
.column {
  gap: var(--space-6);
}
.printers,
.stack,
.tickets {
  gap: var(--space-3);
}
.columns.split {
  display: grid;
  grid-template-columns: minmax(0, 1.55fr) minmax(0, 1fr);
  gap: 28px;
  align-items: start;
}
.tickets.grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-4);
}
</style>
