import { describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import type { ApiResult } from './errors'
import { useSubmit } from './useSubmit'

const flush = () => new Promise((resolve) => setTimeout(resolve))

describe('useSubmit', () => {
  it('is busy while the action runs and returns its result', async () => {
    let release!: (result: ApiResult<number>) => void
    const form = useSubmit(() => new Promise<ApiResult<number>>((resolve) => (release = resolve)))

    const pending = form.submit()
    expect(form.busy.value).toBe(true)
    release({ data: 1 })

    expect((await pending).data).toBe(1)
    expect(form.busy.value).toBe(false)
    expect(form.error.value).toBeNull()
  })

  it('keeps the error until the next submit and ignores submits while busy', async () => {
    let calls = 0
    const form = useSubmit(async (): Promise<ApiResult<void>> => (++calls === 1 ? { error: { kind: 'network' } } : { data: undefined }))

    await form.submit()
    expect(form.error.value).toEqual({ kind: 'network' })

    const second = form.submit()
    void form.submit()
    await second
    expect(calls).toBe(2)
    expect(form.error.value).toBeNull()
  })

  it('leaves the entered values in the form when the submit fails', async () => {
    const Form = defineComponent({
      setup() {
        const name = ref('')
        const form = useSubmit(async (): Promise<ApiResult<void>> => ({ error: { kind: 'problem', status: 400, detail: 'Bad name.' } }))
        return () =>
          h('form', { onSubmit: (event: Event) => (event.preventDefault(), form.submit()) }, [
            h('input', { value: name.value, onInput: (event: Event) => (name.value = (event.target as HTMLInputElement).value) }),
            form.error.value ? h('p', { role: 'alert' }, 'failed') : null,
          ])
      },
    })
    const wrapper = mount(Form)

    await wrapper.find('input').setValue('lukas')
    await wrapper.find('form').trigger('submit')
    await flush()

    expect(wrapper.find('[role=alert]').exists()).toBe(true)
    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('lukas')
  })
})
