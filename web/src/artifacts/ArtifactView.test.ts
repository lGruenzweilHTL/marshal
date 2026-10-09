import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ArtifactView from './ArtifactView.vue'
import { FakeServer } from '../testing/fakeServer'
import { artifact } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

vi.mock('./viewers/modelScene', () => ({
  parseModel: vi.fn(() => ({})),
  stage: vi.fn(() => ({ dispose: vi.fn() })),
}))

const content = (id: string) => `https://marshal.test/api/v1/artifacts/${id}/content`

function server(item: ReturnType<typeof artifact>, text?: string) {
  const fake = new FakeServer().antiforgery('csrf').on('GET', '/api/v1/me', 200, me).on('GET', `/api/v1/artifacts/${item.id}`, 200, item)
  return text === undefined ? fake : fake.handle('GET', `/api/v1/artifacts/${item.id}/content`, () => new Response(text, { status: 200 }))
}

async function mountViewer(fake: FakeServer, id: string, hash = '') {
  return (await mountWithRouter(fake, id, hash)).wrapper
}

async function mountWithRouter(fake: FakeServer, id: string, hash = '') {
  const mounted = await mountOptions(fake, `/artifacts/${id}${hash}`)
  const wrapper = mount(ArtifactView, { ...mounted.options, attachTo: document.body })
  await settle()
  return { wrapper, router: mounted.router }
}

// Lazy viewers: the dynamic import, then the content fetch.
async function settle() {
  for (let round = 0; round < 3; round++) {
    await flushPromises()
    await vi.dynamicImportSettled()
  }
  await flushPromises()
}

afterEach(() => {
  document.body.innerHTML = ''
  localStorage.clear()
})

describe('ArtifactView', () => {
  it('shows images and SVG through <img> from Marshal', async () => {
    for (const kind of ['image', 'svg']) {
      const wrapper = await mountViewer(server(artifact({ id: kind, kind, name: `x.${kind}`, tainted: false, source: 'upload' })), kind)
      expect(wrapper.get('[data-test="image"]').element.tagName).toBe('IMG')
      expect(wrapper.get('[data-test="image"]').attributes('src')).toBe(content(kind))
      expect(wrapper.find('iframe').exists()).toBe(false)
      wrapper.unmount()
    }
  })

  it('plays video with controls', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'v', kind: 'video', name: 'clip.mp4' })), 'v')

    const video = wrapper.get('[data-test="video"]')
    expect(video.attributes('src')).toBe(content('v'))
    expect(video.attributes('controls')).toBeDefined()
  })

  it('highlights code with line numbers, language and line count', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'Program.cs' }), 'var x = 1;\nreturn x;\n'), 'c')

    const code = wrapper.get('[data-test="code"]')
    expect(code.attributes('data-language')).toBe('csharp')
    expect(code.findAll('[data-test="line-number"]').map((cell) => cell.text())).toEqual(['1', '2'])
    expect(code.get('.tok-keyword').text()).toBe('var')
    expect(wrapper.get('[data-test="language"]').text()).toBe('C#')
    expect(wrapper.get('[data-test="line-count"]').text()).toBe('2 lines')
  })

  it('shows unknown extensions as plain text', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'build.log' }), 'if "x"\n'), 'c')

    expect(wrapper.find('[data-test="code"] [class^="tok-"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="language"]').text()).toBe('Plain text')
  })

  it('draws indent guides per line', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'Program.cs' }), 'class A\n{\n    int x;\n}\n'), 'c')

    const cells = wrapper.findAll('[data-test="code"] td.line')
    expect(cells[2]!.attributes('style')).toContain('--guides: 1')
    expect(cells[0]!.attributes('style')).toBeUndefined()
  })

  it('copies the full text', async () => {
    const writeText = vi.fn(() => Promise.resolve())
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'Program.cs' }), 'var x = 1;\n'), 'c')

    await wrapper.get('[data-test="copy"]').trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith('var x = 1;\n')
    expect(wrapper.get('[data-test="copy"]').text()).toBe('Copied')
  })

  it('remembers the wrap toggle for the next code artifact', async () => {
    const first = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'Program.cs' }), 'var x = 1;\n'), 'c')
    expect(first.get('[data-test="code"]').classes()).not.toContain('wrap')

    await first.get('[data-test="wrap"]').trigger('click')
    expect(first.get('[data-test="code"]').classes()).toContain('wrap')
    expect(first.get('[data-test="wrap"]').attributes('aria-pressed')).toBe('true')
    first.unmount()

    const second = await mountViewer(server(artifact({ id: 'd', kind: 'code', name: 'App.ts' }), 'let y = 2\n'), 'd')
    expect(second.get('[data-test="code"]').classes()).toContain('wrap')
  })

  it('highlights the lines of a #L link', async () => {
    const text = Array.from({ length: 10 }, (_, index) => `line ${index + 1}`).join('\n')
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'notes.cs' }), text), 'c', '#L3-L5')

    const selected = wrapper.findAll('[data-test="line"].selected').map((row) => row.attributes('id'))
    expect(selected).toEqual(['L3', 'L4', 'L5'])
  })

  it('ignores a #L link outside the file', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'c', kind: 'code', name: 'notes.cs' }), 'a\nb\n'), 'c', '#L5')

    expect(wrapper.findAll('[data-test="line"].selected')).toEqual([])
  })

  it('selects lines by clicking and shift-clicking line numbers', async () => {
    const text = Array.from({ length: 10 }, (_, index) => `line ${index + 1}`).join('\n')
    const { wrapper, router } = await mountWithRouter(server(artifact({ id: 'c', kind: 'code', name: 'notes.cs' }), text), 'c')

    const numbers = wrapper.findAll('[data-test="line-number"]')
    await numbers[4]!.trigger('click')
    await numbers[8]!.trigger('click', { shiftKey: true })
    await flushPromises()

    expect(router.currentRoute.value.hash).toBe('#L5-L9')
    expect(wrapper.findAll('[data-test="line"].selected')).toHaveLength(5)
  })

  it('shows diffs with highlighted code and tinted rows', async () => {
    const diff = '--- a/src/App.cs\n+++ b/src/App.cs\n@@ -1 +1 @@\n-old\n+var x = 1;\n'
    const wrapper = await mountViewer(server(artifact({ id: 'd', kind: 'diff', name: 'change.diff' }), diff), 'd')

    const added = wrapper.get('[data-test="diff"] .added')
    expect(added.text()).toBe('+var x = 1;')
    expect(added.get('.marker').text()).toBe('+')
    expect(added.get('.tok-keyword').text()).toBe('var')
    expect(wrapper.get('[data-test="diff"] .removed').text()).toBe('-old')
  })

  it('highlights fenced code in Markdown', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'm', kind: 'markdown', name: 'notes.md' }), '# Notes\n\n```ts\nconst x = 1\n```\n'), 'm')

    const markdown = wrapper.get('[data-test="markdown"]')
    expect(markdown.get('pre code .tok-keyword').text()).toBe('const')
  })

  it('renders Markdown with external images as links', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'm', kind: 'markdown', name: 'notes.md' }), '# Notes\n\n![x](https://tracker.example/p.png)\n'), 'm')

    const markdown = wrapper.get('[data-test="markdown"]')
    expect(markdown.find('h1').text()).toBe('Notes')
    expect(markdown.find('img').exists()).toBe(false)
    expect(markdown.get('a').attributes('href')).toBe('https://tracker.example/p.png')
  })

  it('shows HTML in a sandboxed iframe without scripts', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'h', kind: 'html', name: 'mock.html' })), 'h')

    const frame = wrapper.get('iframe[data-test="html"]')
    expect(frame.attributes('sandbox')).toBe('')
    expect(frame.attributes('src')).toBe(content('h'))
  })

  it('opens PDFs in a new tab and offers only a download for other files', async () => {
    const pdf = await mountViewer(server(artifact({ id: 'p', kind: 'pdf', name: 'invoice.pdf' })), 'p')
    expect(pdf.get('[data-test="pdf"]').attributes()).toMatchObject({ href: content('p'), target: '_blank' })
    pdf.unmount()

    const other = await mountViewer(server(artifact({ id: 'o', kind: 'other', name: 'data.bin' })), 'o')
    expect(other.find('[data-test="no-preview"]').exists()).toBe(true)
    expect(other.get('[data-test="download"]').attributes('href')).toBe(content('o'))
  })

  it('marks source and third-party content on every viewer', async () => {
    const wrapper = await mountViewer(server(artifact({ id: 'a1', kind: 'image', name: 'scan.png', source: 'mail', tainted: true })), 'a1')

    expect(wrapper.get('[data-test="source"]').text()).toBe('Mail')
    expect(wrapper.get('[data-test="tainted"]').text()).toBe('Third-party content')
  })

  it('loads a 3D model and shows the controls hint; large models only on request', async () => {
    const small = await mountViewer(server(artifact({ id: 'a1' }), 'solid x'), 'a1')
    expect(small.get('[data-test="controls"]').text()).toContain('Drag to rotate')
    small.unmount()

    const large = await mountViewer(server(artifact({ id: 'big', size: 60 * 1024 * 1024 }), 'solid x'), 'big')
    expect(large.find('[data-test="controls"]').exists()).toBe(false)
    await large.get('[data-test="load-model"]').trigger('click')
    await settle()
    expect(large.find('[data-test="controls"]').exists()).toBe(true)
  })
})
