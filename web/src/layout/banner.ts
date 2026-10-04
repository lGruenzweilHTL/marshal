import { inject, shallowRef, type App, type InjectionKey, type Ref } from 'vue'
import type { Router } from 'vue-router'
import { fromThrown, type ApiError } from '../api/errors'

export interface BannerEntry {
  id: number
  error: ApiError
}

/** Errors no view handled, shown at the top of every page until dismissed. */
export interface ErrorBanner {
  readonly entries: Readonly<Ref<BannerEntry[]>>
  /** Adds anything thrown (an `ApiFailure` keeps its API error, everything else is `unexpected`). */
  report(cause: unknown): void
  dismiss(id: number): void
}

export const bannerKey: InjectionKey<ErrorBanner> = Symbol('banner')

export function createErrorBanner(): ErrorBanner {
  const entries = shallowRef<BannerEntry[]>([])
  let next = 0
  return {
    entries,
    report(cause) {
      // The same failure reported twice in a row (a render loop) shows once.
      const error = fromThrown(cause)
      const last = entries.value.at(-1)
      if (last && JSON.stringify(last.error) === JSON.stringify(error) && error.kind !== 'unexpected') return
      entries.value = [...entries.value, { id: ++next, error }]
    },
    dismiss(id) {
      entries.value = entries.value.filter((entry) => entry.id !== id)
    },
  }
}

/** Routes uncaught errors from Vue, promises and navigation into the banner (and still logs them). */
export function installGlobalErrorHandlers(app: App, router: Router, banner: ErrorBanner, target: EventTarget = window): void {
  app.config.errorHandler = (error) => {
    console.error(error)
    banner.report(error)
  }
  router.onError((error) => {
    console.error(error)
    banner.report(error)
  })
  target.addEventListener('unhandledrejection', (event) => {
    const rejection = event as PromiseRejectionEvent
    rejection.preventDefault()
    console.error(rejection.reason)
    banner.report(rejection.reason)
  })
}

export function useErrorBanner(): ErrorBanner {
  const banner = inject(bannerKey)
  if (!banner) throw new Error('No error banner provided.')
  return banner
}
