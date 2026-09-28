import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PreferencesView from './PreferencesView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const stored = {
  language: 'en',
  timeZone: 'Europe/Vienna',
  quietHours: [{ day: 'sunday', start: '22:00:00', end: '07:00:00' }],
  notificationTargets: { person: 'person.lukas', push: ['notify.mobile_app_pixel'] },
  dndUntil: null as string | null,
}

function server() {
  return new FakeServer().antiforgery('t').on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/me/preferences', 200, stored)
}

async function mountPreferences(fake: FakeServer) {
  const { options } = await mountOptions(fake, '/preferences')
  const wrapper = mount(PreferencesView, options)
  await flushPromises()
  return wrapper
}

function sent(fake: FakeServer) {
  return JSON.parse(fake.request('PUT', '/api/v1/me/preferences')!.body) as typeof stored
}

describe('PreferencesView', () => {
  it('saves a changed language with the rest unchanged', async () => {
    const fake = server().on('PUT', '/api/v1/me/preferences', 200, { ...stored, language: 'de' })
    const wrapper = await mountPreferences(fake)

    expect((wrapper.get('input[name="timeZone"]').element as HTMLInputElement).value).toBe('Europe/Vienna')
    await wrapper.get('select[name="language"]').setValue('de')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(sent(fake)).toEqual({ ...stored, language: 'de' })
    expect(wrapper.get('[role="status"]').text()).toContain('Saved')
  })

  it('shows the validation error for an unknown time zone', async () => {
    const fake = server().on('PUT', '/api/v1/me/preferences', 400, { code: 'preferences.invalid', detail: "timeZone 'Mars/Base' is not a known IANA time zone." })
    const wrapper = await mountPreferences(fake)

    await wrapper.get('input[name="timeZone"]').setValue('Mars/Base')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('Mars/Base')
  })

  it('edits quiet hours, push targets and do-not-disturb', async () => {
    const fake = server().on('PUT', '/api/v1/me/preferences', 200, stored)
    const wrapper = await mountPreferences(fake)

    await wrapper.get('[data-test="add-window"]').trigger('click')
    const windows = wrapper.findAll('[data-window]')
    expect(windows).toHaveLength(2)
    await windows[1]!.get('select').setValue('monday')
    await windows[1]!.get('input[name="start"]').setValue('23:00')
    await windows[1]!.get('input[name="end"]').setValue('06:30')
    await wrapper.get('[data-window="0"] [data-test="remove-window"]').trigger('click')
    await wrapper.get('textarea[name="push"]').setValue('notify.a\n\nnotify.b ')
    await wrapper.get('input[name="dndUntil"]').setValue('2026-09-28T22:00')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const body = sent(fake)
    expect(body.quietHours).toEqual([{ day: 'monday', start: '23:00:00', end: '06:30:00' }])
    expect(body.notificationTargets).toEqual({ person: 'person.lukas', push: ['notify.a', 'notify.b'] })
    expect(new Date(body.dndUntil!).getTime()).toBe(new Date(2026, 8, 28, 22, 0).getTime())
  })

  it('has no briefing or interest settings', async () => {
    const wrapper = await mountPreferences(server())

    expect(wrapper.text().toLowerCase()).not.toMatch(/briefing|interest/)
  })
})
