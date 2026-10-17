<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import McpToolApprovals from './McpToolApprovals.vue'
import { Badge, Banner, Card, DataTable, LoadBoundary, Page, PageHeader, Section, useDesktop, type Tone } from '../ui'

/**
 * Read-only view of `/config` and `marshal.yaml` for admins: what loaded, what was skipped and why. Secrets by name only.
 * The only write action is deciding MCP tool approvals.
 */
type Summary = Schemas['ConfigSummary']

const api = useApi()
const desktop = useDesktop()
const { t } = useI18n()

const summary = useLoad<Summary>(() => api.call((client) => client.GET('/api/v1/config')), { isEmpty: () => false })
const forbidden = computed(() => summary.error.value?.kind === 'problem' && summary.error.value.status === 403)

const stateTones: Record<Schemas['McpServerState'], Tone> = { connected: 'success', connecting: 'warning', down: 'danger' }
const tierTones: Record<string, Tone> = { safe: 'success', notify: 'info', confirm: 'warning', forbidden: 'danger' }

const sections = ['models', 'mcp', 'risk', 'templates', 'skills', 'satellites'] as const
function counts(data: Summary): Record<(typeof sections)[number], number> {
  return {
    models: data.models.length,
    mcp: data.mcpServers.length,
    risk: data.riskRules.length,
    templates: data.templates.length,
    skills: data.skills.length,
    satellites: data.satellites.length,
  }
}

const column = (key: string, label: string, align?: 'left' | 'right') => ({ key, label: t(`system.config.columns.${label}`), align })
const columns = computed(() => ({
  models: [column('key', 'key'), column('tier', 'tier'), column('provider', 'provider'), column('model', 'model'), column('endpoint', 'endpoint'), column('apiKeySecret', 'apiKey'), column('contextLength', 'context', 'right')],
  mcp: [column('id', 'server'), column('state', 'status'), column('toolCount', 'tools', 'right'), column('transport', 'transport'), column('target', 'target'), column('secrets', 'secrets'), column('untrusted', 'untrusted')],
  risk: [column('index', 'number', 'right'), column('tool', 'tool'), column('match', 'match'), column('args', 'args'), column('tier', 'tier')],
  templates: [column('name', 'name'), column('modelTier', 'tier'), column('tools', 'tools'), column('skills', 'skills'), column('maxSteps', 'maxSteps', 'right'), column('maxAdvisorCalls', 'advisor', 'right'), column('description', 'description')],
  satellites: [column('id', 'satellite'), column('verbosity', 'verbosity'), column('denyTools', 'hidden'), column('denyConfirm', 'noConfirm'), column('notes', 'notes')],
}))

const rules = (data: Summary) => data.riskRules.map((rule, index) => ({ ...rule, index }))
const conditions = (values: Record<string, string[]>) =>
  Object.entries(values)
    .map(([key, allowed]) => `${key}: ${allowed.join(' | ')}`)
    .join(', ') || '—'
const list = (values: string[] | null | undefined, empty = '—') => (values && values.length ? values.join(', ') : empty)
// Whole server, some tools (globs on hover) or none.
const untrustedLabel = (server: Schemas['McpServerSummary']) =>
  server.untrusted
    ? t('system.config.all')
    : server.untrustedTools.length
      ? t('system.config.some', { count: server.untrustedTools.length })
      : t('system.config.no')
const serverSecrets = (server: Schemas['McpServerSummary']) => list([...(server.authTokenSecret ? [server.authTokenSecret] : []), ...server.environmentSecrets])
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('system.config.title')" :subtitle="t('system.config.subtitle')" back="/system" :back-label="t('system.config.back')">
      <template v-if="desktop" #actions><Badge label="admin" /></template>
    </PageHeader>

    <LoadBoundary :load="summary" size="page" :error-title="forbidden ? t('system.config.forbidden') : undefined">
      <template #default="{ data }">
        <div class="layout" :class="{ two: desktop }">
          <nav v-if="desktop" class="sections" :aria-label="t('system.config.sections')">
            <a v-for="section in sections" :key="section" :href="`#${section}`" class="section-link">
              <span>{{ t(`system.config.${section}`) }}</span>
              <span class="num ink-3">{{ counts(data)[section] }}</span>
            </a>
          </nav>

          <div class="body">
            <Banner v-if="data.errors.length" :title="t('system.config.skipped', { count: data.errors.length }, data.errors.length)" data-test="skipped">
              <ul class="errors">
                <li v-for="entry in data.errors" :key="entry.file" :data-error="entry.file">
                  <span class="t-mono-s">{{ entry.file }}</span>: {{ entry.message }}
                  <span class="ink-2">({{ entry.previousVersionActive ? t('system.config.previousActive') : t('system.config.skippedFile') }})</span>
                </li>
              </ul>
            </Banner>

            <Section id="models" :title="t('system.config.models')" :count="data.models.length">
              <Card flush>
                <DataTable :columns="columns.models" :rows="data.models" :row-key="(model) => model.key" :label="t('system.config.models')" row-attribute="model">
                  <template #cell-key="{ row }"><span class="t-mono-s">{{ row.key }}</span></template>
                  <template #cell-tier="{ row }"><Badge v-if="row.tier" :tone="row.tier === 'advisor' ? 'accent' : 'info'" :label="row.tier" /><span v-else>—</span></template>
                  <template #cell-model="{ row }">
                    <span class="t-mono-s">{{ row.model }}</span>
                    <span v-if="row.vision" class="ink-3"> ({{ t('system.config.vision') }})</span>
                  </template>
                  <template #cell-endpoint="{ row }"><span class="t-mono-s ink-2">{{ row.endpoint ?? '—' }}</span></template>
                  <template #cell-apiKeySecret="{ row }"><span class="t-mono-s">{{ row.apiKeySecret ?? '—' }}</span></template>
                  <template #cell-contextLength="{ row }"><span class="num">{{ row.contextLength }}</span></template>
                </DataTable>
              </Card>
            </Section>

            <Section id="mcp" :title="t('system.config.mcp')" :count="data.mcpServers.length">
              <McpToolApprovals @decided="summary.refresh()" />
              <p v-if="!data.mcpServers.length" class="t-body-s ink-3">{{ t('system.config.empty.mcp') }}</p>
              <Card v-else flush>
                <DataTable :columns="columns.mcp" :rows="data.mcpServers" :row-key="(server) => server.id" :label="t('system.config.mcp')" row-attribute="server">
                  <template #cell-id="{ row }">
                    <span class="t-mono-s strong">{{ row.id }}</span>
                    <span v-if="row.namespace !== row.id" class="ink-3"> ({{ row.namespace }}.*)</span>
                  </template>
                  <template #cell-state="{ row }">
                    <Badge :tone="stateTones[row.state]" dot :label="t(`system.config.state.${row.state}`)" />
                    <span v-if="row.error" class="error t-body-s ink-2">{{ row.error }}</span>
                    <span v-for="warning in row.warnings" :key="warning" class="warning t-body-s ink-2">{{ warning }}</span>
                  </template>
                  <template #cell-toolCount="{ row }"><span class="num">{{ row.toolCount }}</span></template>
                  <template #cell-target="{ row }"><span class="t-mono-s ink-2 wrap-anywhere">{{ row.target }}</span></template>
                  <template #cell-secrets="{ row }"><span class="t-mono-s">{{ serverSecrets(row) }}</span></template>
                  <template #cell-untrusted="{ row }">
                    <span class="untrusted" :title="row.untrustedTools.length ? row.untrustedTools.join(', ') : undefined">{{ untrustedLabel(row) }}</span>
                  </template>
                </DataTable>
              </Card>
            </Section>

            <Section id="risk" :title="t('system.config.risk')" :count="data.riskRules.length">
              <p class="t-body-s ink-2">{{ t('system.config.riskHint') }}</p>
              <Card flush>
                <DataTable :columns="columns.risk" :rows="rules(data)" :row-key="(rule) => String(rule.index)" :label="t('system.config.risk')" row-attribute="rule">
                  <template #cell-index="{ row }"><span class="num">{{ row.index + 1 }}</span></template>
                  <template #cell-tool="{ row }"><span class="t-mono-s">{{ row.tool }}</span></template>
                  <template #cell-match="{ row }"><span class="t-mono-s">{{ conditions(row.match) }}</span></template>
                  <template #cell-args="{ row }"><span class="t-mono-s">{{ conditions(row.args) }}</span></template>
                  <template #cell-tier="{ row }"><Badge v-if="row.tier" :tone="tierTones[row.tier] ?? 'neutral'" :label="row.tier" /><span v-else>—</span></template>
                </DataTable>
              </Card>
            </Section>

            <Section id="templates" :title="t('system.config.templates')" :count="data.templates.length">
              <Card flush>
                <DataTable :columns="columns.templates" :rows="data.templates" :row-key="(template) => template.name" :label="t('system.config.templates')" row-attribute="template">
                  <template #cell-name="{ row }">
                    <span class="t-mono-s strong">{{ row.name }}</span>
                    <span v-if="row.builtIn" class="ink-3"> ({{ t('system.config.builtIn') }})</span>
                  </template>
                  <template #cell-modelTier="{ row }">{{ row.modelTier ?? t('system.config.byInput') }}</template>
                  <template #cell-tools="{ row }"><span class="t-mono-s">{{ list(row.tools) }}</span></template>
                  <template #cell-skills="{ row }">{{ row.skills === null ? t('system.config.all') : list(row.skills, t('system.config.noneList')) }}</template>
                  <template #cell-maxSteps="{ row }"><span class="num">{{ row.maxSteps ?? t('system.config.default') }}</span></template>
                  <template #cell-maxAdvisorCalls="{ row }"><span class="num">{{ row.maxAdvisorCalls ?? t('system.config.default') }}</span></template>
                </DataTable>
              </Card>
            </Section>

            <Section id="skills" :title="t('system.config.skills')" :count="data.skills.length">
              <p v-if="!data.skills.length" class="t-body-s ink-3">{{ t('system.config.empty.skills') }}</p>
              <Card v-else flush>
                <ul class="skills">
                  <li v-for="skill in data.skills" :key="skill.name" :data-skill="skill.name">
                    <span class="t-mono-s strong">{{ skill.name }}</span>
                    <span>{{ skill.description }}</span>
                    <span v-if="skill.files.length" class="t-body-s ink-3">{{ t('system.config.files', { files: skill.files.join(', ') }) }}</span>
                  </li>
                </ul>
              </Card>
            </Section>

            <Section id="satellites" :title="t('system.config.satellites')" :count="data.satellites.length">
              <p v-if="!data.satellites.length" class="t-body-s ink-3">{{ t('system.config.empty.satellites') }}</p>
              <Card v-else flush>
                <DataTable :columns="columns.satellites" :rows="data.satellites" :row-key="(satellite) => satellite.id" :label="t('system.config.satellites')" row-attribute="satellite">
                  <template #cell-id="{ row }"><span class="t-mono-s">{{ row.id }}</span></template>
                  <template #cell-denyTools="{ row }"><span class="t-mono-s">{{ list(row.denyTools) }}</span></template>
                  <template #cell-denyConfirm="{ row }"><span class="t-mono-s">{{ list(row.denyConfirm) }}</span></template>
                  <template #cell-notes="{ row }">{{ row.notes ?? '' }}</template>
                </DataTable>
              </Card>
            </Section>
          </div>
        </div>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.layout {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}
.layout.two {
  display: grid;
  grid-template-columns: 200px minmax(0, 1fr);
  gap: var(--space-8);
  align-items: start;
}
.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  min-width: 0;
}
.sections {
  position: sticky;
  top: var(--space-8);
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.section-link {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 36px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-sm);
  color: var(--ink-2);
  font: var(--text-body-s);
  text-decoration: none;
}
.section-link:hover {
  background: var(--surface);
  color: var(--ink);
}
.errors {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: var(--space-2) 0 0;
  padding: 0;
  list-style: none;
  overflow-wrap: anywhere;
}
.strong {
  font-weight: 600;
}
.error,
.warning {
  display: block;
  margin-top: var(--space-1);
}
.skills {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0;
  list-style: none;
}
.skills li {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--line);
}
.skills li:first-child {
  border-top: 0;
}
/* Long MCP targets (URLs) wrap inside their cell instead of running past the card. */
.wrap-anywhere {
  overflow-wrap: anywhere;
}
</style>
