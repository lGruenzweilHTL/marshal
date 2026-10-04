import { flushPromises, mount } from '@vue/test-utils'
import { Check, Home, Inbox } from 'lucide-vue-next'
import { describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createAppI18n } from '../i18n'
import { AppButton, Badge, Banner, Dialog, ErrorBanner, Field, ListRow, LoadBoundary, LoadState, Logo, PageHeader, Segmented, SideNav, Switch, TabBar } from './index'
import { useLoad } from '../api/useLoad'
import type { ApiResult } from '../api/errors'

const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:any(.*)*', component: { render: () => null } }] })
const global = (language: 'en' | 'de' = 'en') => ({ plugins: [createAppI18n(language), router] })

describe('AppButton', () => {
  it('renders variants, sizes and a block width as classes', () => {
    const wrapper = mount(AppButton, { props: { variant: 'primary', size: 'lg', block: true, label: 'Approve' }, global: global() })
    expect(wrapper.classes()).toEqual(expect.arrayContaining(['button', 'primary', 'lg', 'block']))
    expect(wrapper.text()).toBe('Approve')
  })

  it('is busy and disabled while loading, with a spinner instead of the icon', () => {
    const wrapper = mount(AppButton, { props: { loading: true, icon: Check, label: 'Save' }, global: global() })
    expect(wrapper.attributes('aria-busy')).toBe('true')
    expect(wrapper.attributes('disabled')).toBeDefined()
    expect(wrapper.find('.spinner').exists()).toBe(true)
  })

  it('names an icon-only button by its label', () => {
    const wrapper = mount(AppButton, { props: { iconOnly: true, icon: Check, label: 'Bestätigen' }, global: global('de') })
    expect(wrapper.attributes('aria-label')).toBe('Bestätigen')
    expect(wrapper.text()).toBe('')
    expect(wrapper.classes()).toContain('icon-only')
  })

  it('becomes a link with `to` or `href`', async () => {
    const link = mount(AppButton, { props: { to: '/tasks', label: 'Tasks' }, global: global() })
    const anchor = mount(AppButton, { props: { href: 'https://voron.local', label: 'Mainsail' }, global: global() })
    await flushPromises()
    expect(link.element.tagName).toBe('A')
    expect(anchor.attributes('href')).toBe('https://voron.local')
  })
})

describe('Field', () => {
  it('labels the control, shows the error as an alert and marks it invalid', () => {
    const wrapper = mount(Field, { props: { label: 'Benutzername', error: 'Pflichtfeld', hint: 'Klein geschrieben', modelValue: 'lukas' }, global: global('de') })
    const input = wrapper.get('input')
    expect(wrapper.get('label').attributes('for')).toBe(input.attributes('id'))
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('[role=alert]').text()).toBe('Pflichtfeld')
    expect(input.attributes('aria-describedby')).toContain('-error')
  })

  it('binds the value with v-model', async () => {
    const value = ref('')
    const wrapper = mount(Field, { props: { label: 'Name', modelValue: value.value, 'onUpdate:modelValue': (next: unknown) => (value.value = next as string) }, global: global() })
    await wrapper.get('input').setValue('Voron')
    expect(value.value).toBe('Voron')
  })

  it('renders a select with options from the slot and a textarea', () => {
    const select = mount(Field, { props: { label: 'Tier', kind: 'select' }, slots: { default: '<option value="fast">fast</option>' }, global: global() })
    const area = mount(Field, { props: { label: 'Goal', kind: 'textarea', disabled: true }, global: global() })
    expect(select.findAll('option')).toHaveLength(1)
    expect(area.get('textarea').attributes('disabled')).toBeDefined()
  })
})

describe('Badge, Banner, Segmented, Switch', () => {
  it('tones a badge', () => {
    expect(mount(Badge, { props: { tone: 'danger', dot: true, label: 'failed' } }).classes()).toContain('danger')
  })

  it('makes a danger banner an alert and others a status, with a German dismiss label', async () => {
    const danger = mount(Banner, { props: { tone: 'danger', title: 'Fehler', dismissible: true }, global: global('de') })
    const info = mount(Banner, { props: { tone: 'info', title: 'Hinweis' }, global: global('de') })
    expect(danger.attributes('role')).toBe('alert')
    expect(info.attributes('role')).toBe('status')
    expect(danger.get('[data-test=dismiss]').attributes('aria-label')).toBe('Ausblenden')
    await danger.get('[data-test=dismiss]').trigger('click')
    expect(danger.emitted('dismiss')).toHaveLength(1)
  })

  it('shows the message and trace id of a failed call', () => {
    const wrapper = mount(ErrorBanner, { props: { error: { kind: 'problem', status: 500, code: 'server.unexpected', traceId: '00-ab-01' } }, global: global() })
    expect(wrapper.text()).toContain('Marshal ran into an unexpected error.')
    expect(wrapper.text()).toContain('Trace id 00-ab-01')
  })

  it('marks the selected segment as pressed and switches on click', async () => {
    const wrapper = mount(Segmented, {
      props: { label: 'View', options: [{ value: 'graph', label: 'Graph' }, { value: 'scatter', label: 'Scatter' }], modelValue: 'graph' },
    })
    expect(wrapper.findAll('button').map((button) => button.attributes('aria-pressed'))).toEqual(['true', 'false'])
    await wrapper.findAll('button')[1]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')).toEqual([['scatter']])
  })

  it('exposes a switch as role switch', async () => {
    const wrapper = mount(Switch, { props: { label: 'Pinned', modelValue: false } })
    expect(wrapper.get('input').attributes('role')).toBe('switch')
    await wrapper.get('input').setValue(true)
    expect(wrapper.emitted('update:modelValue')).toEqual([[true]])
  })
})

describe('LoadState and LoadBoundary', () => {
  it('shows a translated error with retry and the trace id', async () => {
    const wrapper = mount(LoadState, { props: { state: 'error', error: { kind: 'network' } }, global: global('de') })
    expect(wrapper.get('[role=alert]').text()).toContain('Konnte nicht geladen werden')
    expect(wrapper.text()).toContain('Marshal ist nicht erreichbar')
    await wrapper.get('[data-test=retry]').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('renders loading, error with retry, empty and the data', async () => {
    let next: ApiResult<string[]> = { error: { kind: 'network' } }
    const View = defineComponent({
      setup() {
        const load = useLoad(async () => next)
        return () =>
          h(LoadBoundary<string[]>, { load, emptyTitle: 'No tasks' }, { default: ({ data }: { data: string[] }) => h('ul', data.map((item) => h('li', item))) })
      },
    })
    const wrapper = mount(View, { global: global() })
    expect(wrapper.find('[aria-busy=true]').exists()).toBe(true)
    await flushPromises()
    expect(wrapper.find('[role=alert]').exists()).toBe(true)

    next = { data: [] }
    await wrapper.get('[data-test=retry]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('No tasks')
  })
})

describe('navigation', () => {
  it('marks the active tab, shows counts and emits select for More', async () => {
    const wrapper = mount(TabBar, {
      props: {
        label: 'Main navigation',
        active: 'home',
        items: [
          { id: 'home', label: 'Home', icon: Home, to: '/' },
          { id: 'inbox', label: 'Inbox', icon: Inbox, to: '/inbox', count: 3, countLabel: '3 items need you' },
          { id: 'more', label: 'More', icon: Home },
        ],
      },
      global: global(),
    })
    await flushPromises()
    expect(wrapper.get('[data-test=tab-home]').attributes('aria-current')).toBe('page')
    expect(wrapper.get('[data-test=tab-inbox] .count').attributes('aria-label')).toBe('3 items need you')
    await wrapper.get('[data-test=tab-more]').trigger('click')
    expect(wrapper.emitted('select')).toEqual([['more']])
  })

  it('groups side navigation items under their labels', async () => {
    const wrapper = mount(SideNav, {
      props: {
        label: 'Hauptnavigation',
        active: 'tasks',
        groups: [
          { id: 'act', label: 'Handeln', items: [{ id: 'home', label: 'Start', icon: Home, to: '/' }] },
          { id: 'activity', label: 'Aktivität', items: [{ id: 'tasks', label: 'Aufgaben', icon: Home, to: '/tasks' }] },
        ],
      },
      global: global('de'),
    })
    await flushPromises()
    expect(wrapper.findAll('.group-label').map((label) => label.text())).toEqual(['Handeln', 'Aktivität'])
    expect(wrapper.get('[data-test=nav-tasks]').attributes('aria-current')).toBe('page')
  })

  it('renders a page header with a back link', async () => {
    const wrapper = mount(PageHeader, { props: { title: 'Lauf', back: '/runs', backLabel: 'Läufe' }, global: global('de') })
    await flushPromises()
    expect(wrapper.get('h1').text()).toBe('Lauf')
    expect(wrapper.get('[data-test=back]').text()).toBe('Läufe')
  })

  it('renders a list row as a link with a chevron', async () => {
    const wrapper = mount(ListRow, { props: { title: 'Morning briefing', subtitle: 'cron', to: '/tasks/1' }, global: global() })
    await flushPromises()
    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.find('.chevron').exists()).toBe(true)
  })
})

describe('Dialog', () => {
  it('is a labelled modal that closes on Escape and on the close button', async () => {
    const wrapper = mount(Dialog, { props: { open: true, title: 'Druck starten?' }, global: global('de'), attachTo: document.body })
    const dialog = document.querySelector('[role=dialog]') as HTMLElement
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.getAttribute('aria-label')).toBe('Druck starten?')
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    ;(document.querySelector('[data-test=close]') as HTMLElement).click()
    expect(wrapper.emitted('close')).toHaveLength(2)
    wrapper.unmount()
  })
})

describe('Logo', () => {
  it('renders each variant from its SVG with an accessible name', () => {
    for (const variant of ['wordmark', 'tile', 'mark'] as const) {
      const wrapper = mount(Logo, { props: { variant, size: 24 } })
      expect(wrapper.attributes('role')).toBe('img')
      expect(wrapper.attributes('aria-label')).toBe('Marshal')
      expect(wrapper.find('svg').exists()).toBe(true)
      expect(wrapper.html()).not.toContain('<!--')
    }
  })
})
