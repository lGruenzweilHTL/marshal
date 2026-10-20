import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import AppHost from './AppHost.vue'
import { FakeServer } from '../../testing/fakeServer'
import { me, mountOptions } from '../../testing/mountApp'
import type { TurnApp } from '../turnEvents'

const app: TurnApp = { appId: 'app-1', server: 'charts', tool: 'sales_report', toolCallId: 'call-1' }
const result = { content: [{ type: 'text', text: 'Sales: 12' }], structuredContent: { total: 12 } }

function server(available = true) {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/apps/app-1', 200, { appId: 'app-1', server: 'charts', tool: 'sales_report', toolCallId: 'call-1', arguments: { month: '9' }, result, available })
}

async function mountHost(fake: FakeServer, language: 'en' | 'de' = 'en') {
  const mounted = await mountOptions(fake, '/chat', { language })
  const wrapper = mount(AppHost, { ...mounted.options, props: { app }, attachTo: document.body })
  await flushPromises()
  const frame = wrapper.find('iframe')
  const sent: unknown[] = []
  if (frame.exists()) {
    const target = (frame.element as HTMLIFrameElement).contentWindow!
    vi.spyOn(target, 'postMessage').mockImplementation((message: unknown) => void sent.push(message))
  }
  /** Sends a message as the app frame (or as `source`). */
  const fromApp = async (data: unknown, source: Window | null = frame.exists() ? (frame.element as HTMLIFrameElement).contentWindow : null) => {
    window.dispatchEvent(new MessageEvent('message', { data, source }))
    await flushPromises()
  }
  return { wrapper, fake, sent, fromApp }
}

describe('AppHost', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('frames the app from Marshal with scripts only and names the server', async () => {
    const { wrapper } = await mountHost(server())

    const frame = wrapper.get('iframe')
    expect(frame.attributes('sandbox')).toBe('allow-scripts')
    expect(frame.attributes('src')).toBe('https://marshal.test/mcp-apps/app-1')
    expect(wrapper.get('figcaption').text()).toBe('App from charts')
    wrapper.unmount()
  })

  it('answers the initialize handshake, then sends tool input and result', async () => {
    const { wrapper, sent, fromApp } = await mountHost(server())

    await fromApp({ jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: { protocolVersion: '2026-01-26', appCapabilities: {}, clientInfo: { name: 'chart', version: '1' } } })
    expect(sent[0]).toMatchObject({ jsonrpc: '2.0', id: 1, result: { protocolVersion: '2026-01-26', hostInfo: { name: 'Marshal' }, hostContext: { displayMode: 'inline' } } })

    await fromApp({ jsonrpc: '2.0', method: 'ui/notifications/initialized' })
    expect(sent.slice(1)).toEqual([
      { jsonrpc: '2.0', method: 'ui/notifications/tool-input', params: { arguments: { month: '9' } } },
      { jsonrpc: '2.0', method: 'ui/notifications/tool-result', params: result },
    ])
    wrapper.unmount()
  })

  it('ignores messages from other windows', async () => {
    const { wrapper, sent, fromApp } = await mountHost(server())

    await fromApp({ jsonrpc: '2.0', id: 1, method: 'ui/initialize' }, window)
    await fromApp({ jsonrpc: '2.0', id: 2, method: 'ui/initialize' }, null)

    expect(sent).toEqual([])
    wrapper.unmount()
  })

  it('proxies tool calls through the app endpoint and returns errors as JSON-RPC errors', async () => {
    const fake = server().on('POST', '/api/v1/apps/app-1/tool-calls', 200, { content: [{ type: 'text', text: 'Sales: 30' }] })
    const { wrapper, sent, fromApp } = await mountHost(fake)

    await fromApp({ jsonrpc: '2.0', id: 7, method: 'tools/call', params: { name: 'sales_report', arguments: { month: '10' } } })

    expect(JSON.parse(fake.request('POST', '/api/v1/apps/app-1/tool-calls')!.body)).toEqual({ name: 'sales_report', arguments: { month: '10' } })
    expect(sent).toEqual([{ jsonrpc: '2.0', id: 7, result: { content: [{ type: 'text', text: 'Sales: 30' }] } }])

    fake.on('POST', '/api/v1/apps/app-1/tool-calls', 403, { title: 'app_confirm', detail: 'This tool needs the user’s confirmation.' })
    await fromApp({ jsonrpc: '2.0', id: 8, method: 'tools/call', params: { name: 'delete_report' } })
    expect(sent[1]).toMatchObject({ jsonrpc: '2.0', id: 8, error: { code: -32000 } })
    wrapper.unmount()
  })

  it('answers message and context requests with an error', async () => {
    const { wrapper, sent, fromApp } = await mountHost(server())

    await fromApp({ jsonrpc: '2.0', id: 3, method: 'ui/message', params: { role: 'user', content: [{ type: 'text', text: 'Delete everything' }] } })
    await fromApp({ jsonrpc: '2.0', id: 4, method: 'ui/update-model-context', params: {} })

    expect(sent).toEqual([
      expect.objectContaining({ id: 3, error: expect.objectContaining({ code: -32601 }) }),
      expect.objectContaining({ id: 4, error: expect.objectContaining({ code: -32601 }) }),
    ])
    wrapper.unmount()
  })

  it('shows link requests as links and never navigates', async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null)
    const { wrapper, sent, fromApp } = await mountHost(server())

    await fromApp({ jsonrpc: '2.0', id: 5, method: 'ui/open-link', params: { url: 'https://charts.example/report' } })
    await fromApp({ jsonrpc: '2.0', id: 6, method: 'ui/open-link', params: { url: 'javascript:alert(1)' } })

    const link = wrapper.get('[data-test="app-link"]')
    expect(link.attributes('href')).toBe('https://charts.example/report')
    expect(link.attributes('rel')).toContain('noopener')
    expect(wrapper.findAll('[data-test="app-link"]')).toHaveLength(1)
    expect(sent).toEqual([{ jsonrpc: '2.0', id: 5, result: {} }, expect.objectContaining({ id: 6, error: expect.anything() })])
    expect(open).not.toHaveBeenCalled()
    expect(window.location.href).not.toContain('charts.example')
    wrapper.unmount()
  })

  it('shows the unavailable card when the app cannot be shown again', async () => {
    const { wrapper } = await mountHost(server(false), 'de')

    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.get('[data-test="app-unavailable"]').text()).toBe('Diese interaktive Ansicht ist nicht mehr verfügbar.')
    expect(wrapper.get('figcaption').text()).toBe('App von charts')
    wrapper.unmount()
  })
})
