import { shallowRef, type Ref } from 'vue'

export type Appearance = 'system' | 'light' | 'dark'

const storageKey = 'marshal.appearance'
const current = shallowRef<Appearance>(read())

function read(): Appearance {
  try {
    const value = localStorage.getItem(storageKey)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

function apply(value: Appearance) {
  if (value === 'system') delete document.documentElement.dataset.theme
  else document.documentElement.dataset.theme = value
}

/** Applies the appearance stored on this device (called once at start). */
export function applyStoredAppearance(): void {
  current.value = read()
  apply(current.value)
}

/** The per-device appearance: System follows the OS, Light and Dark force a theme. Never sent to the API. */
export function useAppearance(): { appearance: Readonly<Ref<Appearance>>; setAppearance(value: Appearance): void } {
  return {
    appearance: current,
    setAppearance(value) {
      current.value = value
      apply(value)
      try {
        if (value === 'system') localStorage.removeItem(storageKey)
        else localStorage.setItem(storageKey, value)
      } catch {
        // Private mode or blocked storage: the choice lasts until the page is closed.
      }
    },
  }
}
