import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createApp } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { ApiFailure } from '../api/errors'
import { createAppI18n } from '../i18n'
import { bannerKey, createErrorBanner, installGlobalErrorHandlers } from './banner'
import GlobalBanner from './GlobalBanner.vue'

describe('global error banner', () => {
  it('collects uncaught errors from Vue, promises and the router', async () => {
    const app = createApp({ render: () => null })
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })
    const banner = createErrorBanner()
    const target = new EventTarget()
    installGlobalErrorHandlers(app, router, banner, target)

    app.config.errorHandler!(new Error('render failed'), null, 'render')
    const rejection = Object.assign(new Event('unhandledrejection'), { reason: new ApiFailure({ kind: 'network' }), preventDefault() {} })
    target.dispatchEvent(rejection)

    expect(banner.entries.value.map((entry) => entry.error.kind)).toEqual(['unexpected', 'network'])
  })

  it('shows each error with its message and trace id and can dismiss it', async () => {
    const banner = createErrorBanner()
    banner.report(new ApiFailure({ kind: 'problem', status: 500, code: 'server.unexpected', traceId: '00-abc-01' }))
    const wrapper = mount(GlobalBanner, { global: { plugins: [createAppI18n('de')], provide: { [bannerKey as symbol]: banner } } })

    expect(wrapper.text()).toContain('Bei Marshal ist ein unerwarteter Fehler aufgetreten.')
    expect(wrapper.text()).toContain('00-abc-01')

    await wrapper.get('[data-test="dismiss"]').trigger('click')
    await flushPromises()
    expect(banner.entries.value).toEqual([])
  })
})
