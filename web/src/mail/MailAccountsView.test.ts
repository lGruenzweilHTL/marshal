import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import MailAccountsView from './MailAccountsView.vue'
import { browserNavigation } from './browserNavigation'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const accounts = [
  {
    id: 'a1',
    alias: 'gmail',
    provider: 'imap',
    strategy: 'oauth_google',
    host: 'imap.gmail.com',
    port: 993,
    security: 'tls',
    userName: 'lukas@gmail.com',
    draftsFolder: null,
    createdAt: '2026-09-20T10:00:00Z',
  },
]

const providers = [
  { name: 'google', configured: true },
  { name: 'microsoft', configured: false },
]

function server(list: unknown[] = accounts) {
  return new FakeServer()
    .antiforgery('t')
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/mail/accounts', 200, list)
    .on('GET', '/api/v1/mail/oauth/providers', 200, providers)
}

async function mountAccounts(fake: FakeServer, path = '/mail/accounts', language?: 'en' | 'de') {
  const { options } = await mountOptions(fake, path, language ? { language } : undefined)
  const wrapper = mount(MailAccountsView, { ...options, attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('MailAccountsView', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('lists accounts with alias, sign-in method and server, never a credential', async () => {
    const wrapper = await mountAccounts(server())

    const row = wrapper.get('[data-account="gmail"]')
    expect(row.text()).toContain('Google')
    expect(row.text()).toContain('imap.gmail.com')
    expect(row.text()).toContain('lukas@gmail.com')
  })

  it('adds an IMAP account and clears the password field', async () => {
    const fake = server([]).on('POST', '/api/v1/mail/accounts', 201, accounts[0])
    const wrapper = await mountAccounts(fake)

    await wrapper.get('input[name="alias"]').setValue('private')
    await wrapper.get('input[name="host"]').setValue('imap.example.com')
    await wrapper.get('input[name="userName"]').setValue('lukas@example.com')
    await wrapper.get('input[name="password"]').setValue('app-password')
    await wrapper.get('form[data-test="add-imap"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/mail/accounts')!.body)).toEqual({
      alias: 'private',
      host: 'imap.example.com',
      port: 993,
      security: 'tls',
      userName: 'lukas@example.com',
      password: 'app-password',
      draftsFolder: null,
    })
    expect((wrapper.get('input[name="password"]').element as HTMLInputElement).value).toBe('')
    expect(fake.count('GET', '/api/v1/mail/accounts')).toBe(2)
  })

  it('shows the problem when the alias is taken', async () => {
    const fake = server([]).on('POST', '/api/v1/mail/accounts', 409, { title: 'mail.alias_taken', detail: "The alias 'private' is already used by another mail account." })
    const wrapper = await mountAccounts(fake)

    await wrapper.get('input[name="alias"]').setValue('private')
    await wrapper.get('input[name="host"]').setValue('imap.example.com')
    await wrapper.get('input[name="userName"]').setValue('a@b.c')
    await wrapper.get('input[name="password"]').setValue('x')
    await wrapper.get('form[data-test="add-imap"]').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('already used')
  })

  it('starts the OAuth flow and sends the browser to the provider', async () => {
    const go = vi.spyOn(browserNavigation, 'assign').mockImplementation(() => {})
    const fake = server().on('POST', '/api/v1/mail/oauth/google/start', 200, { authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?state=s' })
    const wrapper = await mountAccounts(fake)

    await wrapper.get('input[name="oauthAlias"]').setValue('work')
    await wrapper.get('input[name="oauthEmail"]').setValue('lukas@gmail.com')
    await wrapper.get('[data-provider="google"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/mail/oauth/google/start')!.body)).toEqual({ alias: 'work', email: 'lukas@gmail.com' })
    expect(go).toHaveBeenCalledWith('https://accounts.google.com/o/oauth2/v2/auth?state=s')
  })

  it('disables providers without an OAuth client', async () => {
    const wrapper = await mountAccounts(server())

    expect(wrapper.get('[data-provider="microsoft"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-provider="google"]').attributes('disabled')).toBeUndefined()
  })

  it('reports the result of the OAuth callback', async () => {
    const connected = await mountAccounts(server(), '/mail/accounts?connected=gmail')
    expect(connected.get('[data-test="callback"]').text()).toContain('gmail')
    connected.unmount()

    const failed = await mountAccounts(server(), '/mail/accounts?error=mail.oauth_state_invalid')
    expect(failed.get('[data-test="callback"]').attributes('role')).toBe('alert')
    expect(failed.get('[data-test="callback"]').text()).toContain('expired')
  })

  it('deletes an account only after confirming in a dialog', async () => {
    const fake = server().on('DELETE', '/api/v1/mail/accounts/a1', 204)
    const wrapper = await mountAccounts(fake)

    await wrapper.get('[data-account="gmail"] [data-test="delete"]').trigger('click')
    await flushPromises()
    expect(fake.count('DELETE', '/api/v1/mail/accounts/a1')).toBe(0)

    ;(document.body.querySelector('[data-test="confirm-delete"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('DELETE', '/api/v1/mail/accounts/a1')).toBe(1)
    expect(fake.count('GET', '/api/v1/mail/accounts')).toBe(2)
  })

  it('says that Marshal never sends mail', async () => {
    const wrapper = await mountAccounts(server())

    expect(wrapper.get('[data-test="no-send"]').text()).toContain('never sends')
  })

  it('shows an empty state without accounts', async () => {
    const wrapper = await mountAccounts(server([]))

    expect(wrapper.text()).toContain('No mail accounts yet')
  })

  it('speaks German', async () => {
    const wrapper = await mountAccounts(server(), '/mail/accounts', 'de')

    expect(wrapper.get('h1').text()).toBe('E-Mail-Konten')
    expect(wrapper.get('[data-test="no-send"]').text()).toContain('sendet nie')
  })
})
