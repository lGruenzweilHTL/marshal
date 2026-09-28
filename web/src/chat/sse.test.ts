import { describe, expect, it } from 'vitest'
import { readEvents } from './sse'
import { eventStream } from '../testing/fakeServer'

async function collect(chunks: string[]) {
  const events = []
  for await (const event of readEvents(eventStream(chunks).body!)) events.push(event)
  return events
}

describe('readEvents', () => {
  it('parses named events with JSON data', async () => {
    expect(await collect(['event: delta\ndata: {"text":"Hi"}\n\nevent: final\ndata: {"speech":"Hi"}\n\n'])).toEqual([
      { event: 'delta', data: '{"text":"Hi"}' },
      { event: 'final', data: '{"speech":"Hi"}' },
    ])
  })

  it('joins events split across chunks and CRLF line ends', async () => {
    expect(await collect(['event: del', 'ta\r\ndata: {"te', 'xt":"a"}\r\n', '\r\n'])).toEqual([{ event: 'delta', data: '{"text":"a"}' }])
  })

  it('defaults the event name, joins multi-line data and skips comments', async () => {
    expect(await collect([': keep-alive\n\ndata: one\ndata: two\n\n'])).toEqual([{ event: 'message', data: 'one\ntwo' }])
  })
})
