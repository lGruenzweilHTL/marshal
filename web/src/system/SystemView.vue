<script setup lang="ts">
import { Activity, Bot, CalendarDays, Database, FileCog, FlaskConical, HeartPulse, House, KeyRound, Mail, NotebookText, Plug, Printer, Radio, Rss, Terminal } from 'lucide-vue-next'
import { computed, type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import type { Schemas } from '../api/client'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { useUserSession } from '../session'
import KillSwitch from '../tasks/KillSwitch.vue'
import { Badge, Card, IconTile, KeyValueList, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, useDesktop, type Tone } from '../ui'
import { loadHealth, type HealthCheck, type HealthReport } from './health'

/** Health checks, version and API versions, the kill switch and (for admins) the way to the configuration. */
const api = useApi()
const user = useUserSession()
const desktop = useDesktop()
const { t, te } = useI18n()
const format = useFormat()

const info = useLoad(() => api.call((client) => client.GET('/api/v1/system')), { isEmpty: () => false })
const health = useLoad<HealthReport>(() => loadHealth(api), { isEmpty: () => false })
const tasks = useLoad(() => api.call((client) => client.GET('/api/v1/tasks')), { isEmpty: () => false })
const running = computed(() => (tasks.data.value ?? []).filter((task) => task.status === 'running').length)
const isAdmin = computed(() => user.current.value?.roles.includes('admin') === true)

const tones: Record<HealthCheck['status'], Tone> = { healthy: 'success', degraded: 'warning', unhealthy: 'danger' }
const icons: Record<string, Component> = { database: Database, config: FileCog, secrets: KeyRound, mcp: Plug, memory: Bot, homeassistant: House }
const checkName = (name: string) => (te(`system.checks.${name}`) ? t(`system.checks.${name}`) : name)
const checkDetail = (check: HealthCheck) => [check.description, ...(check.warnings ?? [])].filter(Boolean).join(' · ') || undefined
const sourceIcons: Record<string, Component> = { ha: House, calendar: CalendarDays, printer: Printer, mail: Mail, executor: Terminal, 'uptime-kuma': Activity }
const simulatedIcons: Record<string, Component> = { home_assistant: House, caldav: CalendarDays, printers: Printer, mail: Mail, rss: Rss, notes: NotebookText }
const simulatedName = (name: string) => (te(`system.demo.names.${name}`) ? t(`system.demo.names.${name}`) : name)
const sourceName = (source: string) => (te(`system.sources.names.${source}`) ? t(`system.sources.names.${source}`) : source)
const sourceDetail = (source: Schemas['EventSourceInfo']) =>
  !source.webhook ? undefined : source.lastReceivedAt ? t('system.sources.lastWebhook', { time: format.value.dateTime(source.lastReceivedAt) }) : t('system.sources.noWebhook')
const about = (data: { version: string; supportedApiVersions: string[]; deprecatedApiVersions: string[] }) => [
  { key: 'version', label: t('system.version'), value: data.version, mono: true },
  { key: 'api-versions', label: t('system.api'), value: data.supportedApiVersions.join(', '), mono: true },
  { key: 'deprecated', label: t('system.deprecated'), value: data.deprecatedApiVersions.join(', ') || t('system.none'), mono: data.deprecatedApiVersions.length > 0 },
]
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('system.title')">
      <template v-if="health.data.value" #actions>
        <Badge :tone="tones[health.data.value.status]" dot :label="t(`system.status.${health.data.value.status}`)" data-test="overall" />
      </template>
    </PageHeader>

    <div class="layout" :class="{ two: desktop }">
      <div class="column">
        <Section :title="t('system.health')">
          <LoadBoundary :load="health">
            <template #default="{ data }">
              <ListCard :label="t('system.health')">
                <ListRow v-for="(check, name) in data.checks" :key="name" :data-check="name" :title="checkName(String(name))" :subtitle="checkDetail(check)">
                  <template #lead><IconTile :icon="icons[name] ?? HeartPulse" :tone="check.status === 'healthy' ? 'neutral' : tones[check.status]" /></template>
                  <template #trail><Badge :tone="tones[check.status]" dot :label="t(`system.status.${check.status}`)" /></template>
                </ListRow>
              </ListCard>
            </template>
          </LoadBoundary>
        </Section>

        <Section :title="t('system.background')">
          <KillSwitch :running="running" @changed="tasks.refresh()" />
          <p class="t-body-s ink-3">{{ t('system.backgroundHint') }}</p>
        </Section>
      </div>

      <div class="column">
        <Card :title="t('system.about')">
          <LoadBoundary :load="info">
            <template #default="{ data }">
              <KeyValueList :items="about(data)" />
            </template>
          </LoadBoundary>
        </Card>
        <LoadBoundary v-if="(info.data.value?.simulatedIntegrations.length ?? 0) > 0" :load="info">
          <template #default="{ data }">
            <Section :title="t('system.demo.title')" data-test="simulated">
              <ListCard :label="t('system.demo.title')">
                <ListRow v-for="name in data.simulatedIntegrations" :key="name" :data-simulated="name" :title="simulatedName(name)">
                  <template #lead><IconTile :icon="simulatedIcons[name] ?? FlaskConical" /></template>
                  <template #trail><Badge tone="warning" :label="t('system.demo.simulated')" /></template>
                </ListRow>
              </ListCard>
              <p class="t-body-s ink-3">{{ t('system.demo.hint') }}</p>
            </Section>
          </template>
        </LoadBoundary>
        <Section :title="t('system.sources.title')">
          <LoadBoundary :load="info">
            <template #default="{ data }">
              <ListCard :label="t('system.sources.title')">
                <ListRow v-for="source in data.eventSources" :key="source.source" :data-source="source.source" :title="sourceName(source.source)" :subtitle="sourceDetail(source)">
                  <template #lead><IconTile :icon="sourceIcons[source.source] ?? Radio" /></template>
                  <template #trail>
                    <Badge :tone="source.connected ? 'success' : 'neutral'" dot :label="source.connected ? t('system.sources.connected') : t('system.sources.disconnected')" />
                  </template>
                </ListRow>
              </ListCard>
            </template>
          </LoadBoundary>
        </Section>
        <ListCard v-if="isAdmin">
          <ListRow to="/system/config" :title="t('system.configTitle')" :subtitle="t('system.configBody')">
            <template #lead><IconTile :icon="FileCog" /></template>
          </ListRow>
        </ListCard>
      </div>
    </div>
  </Page>
</template>

<style scoped>
.layout,
.column {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.two {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: var(--space-8);
  align-items: start;
}
</style>
