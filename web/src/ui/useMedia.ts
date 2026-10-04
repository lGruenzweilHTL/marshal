import { onScopeDispose, shallowRef, type Ref } from 'vue'

/** The desktop layout starts here (side navigation instead of the tab bar). */
export const desktopQuery = '(min-width: 900px)'

/** Whether a media query matches, kept up to date. False where `matchMedia` does not exist (tests without a stub). */
export function useMedia(query: string): Readonly<Ref<boolean>> {
  if (typeof window === 'undefined' || !window.matchMedia) return shallowRef(false)
  const list = window.matchMedia(query)
  const matches = shallowRef(list.matches)
  const update = (event: MediaQueryListEvent) => (matches.value = event.matches)
  list.addEventListener('change', update)
  onScopeDispose(() => list.removeEventListener('change', update))
  return matches
}

/** True from 900 px: the desktop layout. */
export function useDesktop(): Readonly<Ref<boolean>> {
  return useMedia(desktopQuery)
}
