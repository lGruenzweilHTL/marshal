/**
 * The sample MCP app of preview mode, served by the preview dev server at `/mcp-apps/*` with the app CSP. It speaks
 * the host protocol like a real app: initialize, wait for the tool result, draw a bar chart from `structuredContent`,
 * and report its height.
 */
export const fakeAppHtml = `<!doctype html>
<html><head><meta charset="utf-8"><style>
  body { margin: 0; padding: 12px; font: 14px/20px system-ui, sans-serif; color: #1d211c; background: #fff; }
  h1 { margin: 0 0 8px; font-size: 15px; }
  .bar { display: flex; align-items: center; gap: 8px; margin: 4px 0; }
  .bar span:first-child { width: 36px; color: #5d645b; }
  .fill { height: 14px; border-radius: 3px; background: #3f7d5a; }
</style></head><body><h1 id="title">Waiting for data…</h1><div id="bars"></div><script>
  let next = 1
  const send = (message) => parent.postMessage(Object.assign({ jsonrpc: '2.0' }, message), '*')
  window.addEventListener('message', (event) => {
    const message = event.data
    if (message && message.id === 1) send({ method: 'ui/notifications/initialized' })
    if (message && message.method === 'ui/notifications/tool-result') draw(message.params.structuredContent || {})
  })
  function draw(data) {
    document.getElementById('title').textContent = data.title || 'Report'
    const bars = document.getElementById('bars')
    const max = Math.max(1, ...(data.values || []).map((value) => value.amount))
    for (const value of data.values || []) {
      const row = document.createElement('div')
      row.className = 'bar'
      const label = document.createElement('span')
      label.textContent = value.label
      const fill = document.createElement('span')
      fill.className = 'fill'
      fill.style.width = Math.round((value.amount / max) * 220) + 'px'
      row.append(label, fill)
      bars.append(row)
    }
    send({ method: 'ui/notifications/size-changed', params: { width: document.body.scrollWidth, height: document.body.scrollHeight } })
  }
  send({ id: next++, method: 'ui/initialize', params: { protocolVersion: '2026-01-26', appCapabilities: {}, clientInfo: { name: 'preview-chart', version: '1' } } })
</script></body></html>`

/** The app CSP Marshal serves app HTML with (`AppEndpoints.ContentSecurityPolicy`). */
export const appContentSecurityPolicy =
  "sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'"
