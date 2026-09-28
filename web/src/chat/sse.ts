/** One server-sent event: its name (`message` when unnamed) and its data lines joined with `\n`. */
export interface ServerEvent {
  event: string
  data: string
}

/** Reads server-sent events from a response body as they arrive (WHATWG event-stream format, no reconnect). */
export async function* readEvents(body: ReadableStream<Uint8Array>): AsyncGenerator<ServerEvent> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  let event = ''
  let data: string[] = []
  try {
    for (;;) {
      const { value, done } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split(/\r\n|\r|\n/)
      buffer = lines.pop() ?? ''
      for (const line of lines) {
        if (line === '') {
          if (data.length > 0) yield { event: event || 'message', data: data.join('\n') }
          event = ''
          data = []
          continue
        }
        if (line.startsWith(':')) continue
        const colon = line.indexOf(':')
        const field = colon < 0 ? line : line.slice(0, colon)
        const text = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '')
        if (field === 'event') event = text
        else if (field === 'data') data.push(text)
      }
    }
  } finally {
    reader.releaseLock()
  }
}
