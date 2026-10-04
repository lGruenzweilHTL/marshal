import { afterEach, describe, expect, it } from 'vitest'
import { applyStoredAppearance, useAppearance } from './appearance'

describe('appearance', () => {
  afterEach(() => {
    localStorage.clear()
    delete document.documentElement.dataset.theme
  })

  it('applies Dark and Light right away and keeps them on this device', () => {
    const { appearance, setAppearance } = useAppearance()

    setAppearance('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(appearance.value).toBe('dark')
    expect(localStorage.getItem('marshal.appearance')).toBe('dark')

    setAppearance('light')
    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('follows the system again when set to System', () => {
    const { setAppearance } = useAppearance()
    setAppearance('dark')

    setAppearance('system')

    expect(document.documentElement.dataset.theme).toBeUndefined()
    expect(localStorage.getItem('marshal.appearance')).toBeNull()
  })

  it('restores the stored choice at start', () => {
    localStorage.setItem('marshal.appearance', 'dark')

    applyStoredAppearance()

    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(useAppearance().appearance.value).toBe('dark')
  })
})
