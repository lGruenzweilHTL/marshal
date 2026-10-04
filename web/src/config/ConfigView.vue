<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'

const session = useApiSession()
const summary = ref<Schemas['ConfigSummary'] | null>(null)
const error = ref<string | null>(null)

const stateBadges: Record<Schemas['McpServerState'], string> = {
  connected: 'healthy',
  connecting: 'degraded',
  down: 'unhealthy',
}

onMounted(async () => {
  const { data, response } = await session.raw.GET('/api/v1/config')
  summary.value = data ?? null
  error.value = data
    ? null
    : response.status === 403
      ? 'The configuration view needs the admin scope.'
      : 'The configuration could not be loaded.'
})

function conditions(values: Record<string, string[]>): string {
  return Object.entries(values)
    .map(([key, allowed]) => `${key}: ${allowed.join(' | ')}`)
    .join(', ')
}

function list(values: string[] | null | undefined, empty = '–'): string {
  return values && values.length ? values.join(', ') : empty
}
</script>

<template>
  <section class="page">
    <h1>Configuration</h1>
    <p class="muted">Read-only view of <code>/config</code> and <code>marshal.yaml</code>. Edit the files to change it; secrets appear by name only.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <template v-if="summary">
      <template v-if="summary.errors.length">
        <h2>Load errors</h2>
        <ul class="plain">
          <li v-for="entry in summary.errors" :key="entry.file" :data-error="entry.file" class="error">
            <code>{{ entry.file }}</code>: {{ entry.message }}
            <span class="muted">({{ entry.previousVersionActive ? 'previous version active' : 'skipped' }})</span>
          </li>
        </ul>
      </template>

      <h2>Skills</h2>
      <p v-if="!summary.skills.length" class="muted">No skills.</p>
      <ul v-else class="plain">
        <li v-for="skill in summary.skills" :key="skill.name" :data-skill="skill.name">
          <strong>{{ skill.name }}</strong>: {{ skill.description }}
          <span v-if="skill.files.length" class="muted"> (files: {{ skill.files.join(', ') }})</span>
        </li>
      </ul>

      <h2>Agent templates</h2>
      <div class="table-scroll">
        <table>
          <thead>
            <tr><th>Name</th><th>Tier</th><th>Tools</th><th>Skills</th><th>Max steps</th><th>Advisor calls</th><th>Description</th></tr>
          </thead>
          <tbody>
            <tr v-for="template in summary.templates" :key="template.name" :data-template="template.name">
              <td>{{ template.name }}<span v-if="template.builtIn" class="muted"> (built-in)</span></td>
              <td>{{ template.modelTier ?? 'by input' }}</td>
              <td>{{ list(template.tools) }}</td>
              <td>{{ template.skills === null ? 'all' : list(template.skills, 'none') }}</td>
              <td class="number">{{ template.maxSteps ?? 'default' }}</td>
              <td class="number">{{ template.maxAdvisorCalls ?? 'default' }}</td>
              <td>{{ template.description }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2>MCP servers</h2>
      <p v-if="!summary.mcpServers.length" class="muted">No MCP servers.</p>
      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr><th>Server</th><th>Status</th><th>Tools</th><th>Transport</th><th>Target</th><th>Secrets</th><th>Untrusted</th></tr>
          </thead>
          <tbody>
            <tr v-for="server in summary.mcpServers" :key="server.id" :data-server="server.id">
              <td>{{ server.id }}<span v-if="server.namespace !== server.id" class="muted"> ({{ server.namespace }}.*)</span></td>
              <td>
                <span class="badge" :class="stateBadges[server.state]">{{ server.state }}</span>
                <span v-if="server.error" class="muted"> {{ server.error }}</span>
              </td>
              <td class="number">{{ server.toolCount }}</td>
              <td>{{ server.transport }}</td>
              <td><code>{{ server.target }}</code></td>
              <td>{{ list([...(server.authTokenSecret ? [server.authTokenSecret] : []), ...server.environmentSecrets]) }}</td>
              <td>{{ server.untrusted ? 'yes' : 'no' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2>Risk rules</h2>
      <p class="muted">Evaluated in order; the first match wins.</p>
      <div class="table-scroll">
        <table>
          <thead>
            <tr><th>#</th><th>Tool</th><th>Target</th><th>Arguments</th><th>Tier</th></tr>
          </thead>
          <tbody>
            <tr v-for="(rule, index) in summary.riskRules" :key="index" :data-rule="index">
              <td class="number">{{ index + 1 }}</td>
              <td><code>{{ rule.tool }}</code></td>
              <td>{{ conditions(rule.match) || '–' }}</td>
              <td>{{ conditions(rule.args) || '–' }}</td>
              <td>{{ rule.tier }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2>Models</h2>
      <div class="table-scroll">
        <table>
          <thead>
            <tr><th>Key</th><th>Tier</th><th>Provider</th><th>Model</th><th>Endpoint</th><th>API key secret</th><th>Context</th></tr>
          </thead>
          <tbody>
            <tr v-for="model in summary.models" :key="model.key" :data-model="model.key">
              <td>{{ model.key }}</td>
              <td>{{ model.tier }}</td>
              <td>{{ model.provider }}</td>
              <td>{{ model.model }}<span v-if="model.vision" class="muted"> (vision)</span></td>
              <td>{{ model.endpoint ?? '–' }}</td>
              <td>{{ model.apiKeySecret ?? '–' }}</td>
              <td class="number">{{ model.contextLength }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <h2>Satellites</h2>
      <p v-if="!summary.satellites.length" class="muted">No satellite profiles.</p>
      <div v-else class="table-scroll">
        <table>
          <thead>
            <tr><th>Satellite</th><th>Verbosity</th><th>Hidden tools</th><th>No confirmation</th><th>Notes</th></tr>
          </thead>
          <tbody>
            <tr v-for="satellite in summary.satellites" :key="satellite.id" :data-satellite="satellite.id">
              <td>{{ satellite.id }}</td>
              <td>{{ satellite.verbosity }}</td>
              <td>{{ list(satellite.denyTools) }}</td>
              <td>{{ list(satellite.denyConfirm) }}</td>
              <td>{{ satellite.notes ?? '' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </section>
</template>
