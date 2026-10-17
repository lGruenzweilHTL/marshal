import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import InputRequestForm from './InputRequestForm.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'
import type { Schemas } from '../api/client'

type Request = Schemas['InputRequestItem']

const form: Request = {
  id: 'i-1',
  serverId: 'paperless',
  tool: 'paperless.upload',
  mode: 'form',
  message: 'Which correspondent is this from? <b>Answer</b>',
  requestedSchema: {
    type: 'object',
    properties: {
      correspondent: { type: 'string', title: 'Correspondent' },
      pages: { type: 'integer', title: 'Pages' },
      archive: { type: 'boolean', title: 'Archive the original' },
      kind: { type: 'string', title: 'Kind', oneOf: [{ const: 'bill', title: 'Bill' }, { const: 'letter', title: 'Letter' }] },
      note: { type: 'string' },
    },
    required: ['correspondent', 'pages'],
  },
  url: null,
  sessionId: 's-1',
  taskId: 't-1',
  status: 'pending',
  createdAt: '2026-10-16T08:00:00Z',
  expiresAt: '2026-10-16T08:10:00Z',
}

const login: Request = { ...form, id: 'i-2', mode: 'url', message: 'Log in to Paperless.', requestedSchema: null, url: 'https://paperless.example/login' }

async function mountForm(request: Request, language: 'en' | 'de' = 'en') {
  const fake = new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .on('POST', `/api/v1/approvals/inputs/${request.id}`, 200, { ...request, status: 'answered' })
    .on('POST', `/api/v1/approvals/inputs/${request.id}/decline`, 200, { ...request, status: 'declined' })
    .antiforgery('token')
  const { options } = await mountOptions(fake, '/inbox', { language })
  const wrapper = mount(InputRequestForm, { ...options, props: { request } })
  await flushPromises()
  return { wrapper, fake }
}

describe('InputRequestForm', () => {
  it('generates the form from the schema: text, number, checkbox and choice', async () => {
    const { wrapper } = await mountForm(form)

    expect(wrapper.get('input[name="correspondent"]').attributes('type')).toBe('text')
    expect(wrapper.get('input[name="pages"]').attributes('type')).toBe('number')
    expect(wrapper.get('input[name="archive"]').attributes('type')).toBe('checkbox')
    expect(wrapper.findAll('select[name="kind"] option').map((option) => option.text())).toEqual(['Bill', 'Letter'])
    expect(wrapper.text()).toContain('Correspondent')
  })

  it('shows the server message as plain text', async () => {
    const { wrapper } = await mountForm(form)

    expect(wrapper.find('[data-test="message"] b').exists()).toBe(false)
    expect(wrapper.get('[data-test="message"]').text()).toContain('<b>Answer</b>')
  })

  it('sends the answer with typed values and leaves empty optional fields out', async () => {
    const { wrapper, fake } = await mountForm(form)

    await wrapper.get('input[name="correspondent"]').setValue('ACME')
    await wrapper.get('input[name="pages"]').setValue('3')
    await wrapper.get('input[name="archive"]').setValue(true)
    await wrapper.get('select[name="kind"]').setValue('letter')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/approvals/inputs/i-1')!.body)).toEqual({
      content: { correspondent: 'ACME', pages: 3, archive: true, kind: 'letter' },
    })
    expect(wrapper.get('[data-test="outcome"]').text()).toContain('Sent')
    expect(wrapper.emitted('settled')).toEqual([['answered']])
  })

  it('opens the server page only on click and sends Done without content', async () => {
    const { wrapper, fake } = await mountForm(login)

    const link = wrapper.get('[data-test="open"]')
    expect(link.attributes('href')).toBe('https://paperless.example/login')
    expect(link.attributes('target')).toBe('_blank')
    expect(link.attributes('rel')).toContain('noopener')
    expect(wrapper.get('[data-test="answer"]').text()).toBe('Done')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/approvals/inputs/i-2')!.body)).toEqual({ content: null })
  })

  it('declines', async () => {
    const { wrapper, fake } = await mountForm(form)

    await wrapper.get('[data-test="decline"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/approvals/inputs/i-1/decline')).toBe(1)
    expect(wrapper.get('[data-test="outcome"]').text()).toContain('Nothing was sent')
  })

  it('shows a closed request without the form', async () => {
    const { wrapper } = await mountForm({ ...form, status: 'expired' })

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.get('[data-test="closed"]').text()).toContain('expired')
  })

  it('speaks German', async () => {
    const { wrapper } = await mountForm(login, 'de')

    expect(wrapper.get('[data-test="answer"]').text()).toBe('Fertig')
    expect(wrapper.get('[data-test="decline"]').text()).toBe('Ablehnen')
    expect(wrapper.text()).toContain('Der Server paperless braucht eine Angabe von dir')
  })
})
