<script setup lang="ts">
import { ExternalLink, LayoutPanelTop } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../../api/client'
import { useApi } from '../../api/inject'
import { AppIcon } from '../../ui'
import type { TurnApp } from '../turnEvents'
import { callFailed, isRequest, methodNotFound, parseMessage, protocolVersion, safeLink, type RpcRequest } from './appProtocol'

/**
 * An MCP app below a reply: the server's HTML in an iframe with `sandbox="allow-scripts"` only (opaque origin: no
 * cookies, storage, forms, popups or navigation; the app CSP blocks every connection). This component is the app's
 * only peer: it answers the MCP Apps requests, proxies tool calls of the app's own server through Marshal's API and
 * shows suggested links for the user to click. Messages from any other window are ignored.
 */
const props = defineProps<{ app: TurnApp }>()
const { t, locale } = useI18n()
const api = useApi()
const frame = useTemplateRef<HTMLIFrameElement>('frame')
const data = shallowRef<Schemas['AppResponse'] | null>(null)
const state = shallowRef<'loading' | 'ready' | 'unavailable'>('loading')
const links = shallowRef<string[]>([])
const height = shallowRef(320)
const src = computed(() => `${api.baseUrl}/mcp-apps/${encodeURIComponent(props.app.appId)}`)

onMounted(async () => {
  window.addEventListener('message', onMessage)
  const result = await api.call((client) => client.GET('/api/v1/apps/{appId}', { params: { path: { appId: props.app.appId } } }))
  data.value = result.data ?? null
  state.value = result.data?.available ? 'ready' : 'unavailable'
})
onBeforeUnmount(() => window.removeEventListener('message', onMessage))

function post(message: unknown) {
  // The frame's origin is opaque ("null"), so no target origin can name it; only its own window receives this.
  frame.value?.contentWindow?.postMessage(message, '*')
}

const reply = (id: RpcRequest['id'], result: unknown) => post({ jsonrpc: '2.0', id, result })
const fail = (id: RpcRequest['id'], code: number, message: string) => post({ jsonrpc: '2.0', id, error: { code, message } })

function onMessage(event: MessageEvent) {
  if (!frame.value || event.source !== frame.value.contentWindow) return
  const message = parseMessage(event.data)
  if (!message) return
  if (!isRequest(message)) return onNotification(message.method, message.params ?? {})
  void onRequest(message)
}

function onNotification(method: string, params: Record<string, unknown>) {
  if (method === 'ui/notifications/initialized' && data.value) {
    post({ jsonrpc: '2.0', method: 'ui/notifications/tool-input', params: { arguments: data.value.arguments } })
    post({ jsonrpc: '2.0', method: 'ui/notifications/tool-result', params: data.value.result })
  } else if (method === 'ui/notifications/size-changed' && typeof params.height === 'number') {
    height.value = Math.min(1200, Math.max(80, Math.ceil(params.height)))
  }
}

async function onRequest(request: RpcRequest) {
  const params = request.params ?? {}
  switch (request.method) {
    case 'ui/initialize':
      return reply(request.id, {
        protocolVersion,
        hostInfo: { name: 'Marshal', version: '1' },
        hostCapabilities: { serverTools: {}, openLinks: {} },
        hostContext: {
          theme: document.documentElement.dataset.theme ?? (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'),
          locale: locale.value,
          displayMode: 'inline',
          availableDisplayModes: ['inline'],
          toolInfo: { id: props.app.toolCallId, tool: { name: props.app.tool } },
        },
      })
    case 'tools/call':
      return callTool(request.id, params)
    case 'ui/open-link': {
      const url = safeLink(params.url)
      if (!url) return fail(request.id, callFailed, 'Only http and https links can be shown.')
      if (!links.value.includes(url)) links.value = [...links.value, url]
      return reply(request.id, {})
    }
    case 'ui/request-display-mode':
      return reply(request.id, { mode: 'inline' })
    default:
      // ui/message, ui/update-model-context, resources/read and anything unknown: apps never reach the conversation.
      return fail(request.id, methodNotFound, `Marshal does not support ${request.method} for apps.`)
  }
}

async function callTool(id: RpcRequest['id'], params: Record<string, unknown>) {
  if (typeof params.name !== 'string') return fail(id, callFailed, 'The tool name is missing.')
  const args = typeof params.arguments === 'object' && params.arguments !== null ? params.arguments : {}
  const result = await api.call((client) =>
    client.POST('/api/v1/apps/{appId}/tool-calls', { params: { path: { appId: props.app.appId } }, body: { name: params.name as string, arguments: args } }),
  )
  const error = result.error
  if (!error) return reply(id, result.data)
  const detail = error.kind === 'problem' ? [error.code, error.detail].filter(Boolean).join(': ') : ''
  fail(id, callFailed, detail || 'The call failed.')
}
</script>

<template>
  <figure class="app" :data-app="app.appId">
    <figcaption class="label">
      <AppIcon :icon="LayoutPanelTop" :size="16" />
      <span>{{ t('chat.apps.label', { server: app.server }) }}</span>
    </figcaption>
    <p v-if="state === 'loading'" class="note">{{ t('chat.apps.loading') }}</p>
    <p v-else-if="state === 'unavailable'" class="note" data-test="app-unavailable">{{ t('chat.apps.unavailable') }}</p>
    <iframe
      v-else
      ref="frame"
      class="frame"
      :src="src"
      sandbox="allow-scripts"
      referrerpolicy="no-referrer"
      :title="t('chat.apps.label', { server: app.server })"
      :style="{ height: `${height}px` }"
    ></iframe>
    <div v-if="links.length" class="links">
      <span>{{ t('chat.apps.link') }}</span>
      <a v-for="link in links" :key="link" :href="link" target="_blank" rel="noopener noreferrer" data-test="app-link">
        <AppIcon :icon="ExternalLink" :size="14" />{{ link }}
      </a>
    </div>
  </figure>
</template>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin: 0;
  max-width: 640px;
  padding: var(--space-3);
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
}
.label {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--ink-3);
  font: 550 12px/16px var(--font-sans);
}
.frame {
  width: 100%;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--surface);
}
.note {
  color: var(--ink-2);
  font: 400 14px/20px var(--font-sans);
}
.links {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font: 400 13px/18px var(--font-sans);
  color: var(--ink-2);
}
.links a {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  overflow-wrap: anywhere;
  color: var(--accent);
}
</style>
