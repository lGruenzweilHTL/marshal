import MarkdownIt from 'markdown-it'

/** Options a renderer can add to the chat's rules; `highlight` returns HTML for a fenced block, or '' to escape it. */
export interface MarkdownOptions {
  highlight?: (code: string, info: string) => string
}

/**
 * A Markdown renderer with the chat's rules: raw HTML escaped, links open in a new tab, only same-origin images. The
 * artifact viewer adds fence highlighting; chat does not, so the highlighter stays out of the entry chunk.
 */
export function createMarkdownRenderer(options: MarkdownOptions = {}): (text: string, origin?: string) => string {
  const markdown = new MarkdownIt({ html: false, linkify: false, highlight: options.highlight ?? null })

  const defaultLink = markdown.renderer.rules.link_open ?? ((tokens, index, renderOptions, _env, self) => self.renderToken(tokens, index, renderOptions))
  markdown.renderer.rules.link_open = (tokens, index, renderOptions, env, self) => {
    tokens[index]!.attrSet('rel', 'noopener noreferrer')
    tokens[index]!.attrSet('target', '_blank')
    return defaultLink(tokens, index, renderOptions, env, self)
  }

  /**
   * Images load only from the app's own origin. Anything else (another host, protocol-relative, `data:`) becomes a link
   * showing its URL, so model output cannot make the browser send data to a third party.
   */
  markdown.renderer.rules.image = (tokens, index, renderOptions, env, self) => {
    const token = tokens[index]!
    const src = String(token.attrGet('src') ?? '')
    if (typeof env?.origin === 'string' && isSameOrigin(src, env.origin)) {
      token.attrSet('alt', self.renderInlineAsText(token.children ?? [], renderOptions, env))
      return self.renderToken(tokens, index, renderOptions)
    }
    const url = markdown.utils.escapeHtml(src)
    return `<a href="${url}" rel="noopener noreferrer" target="_blank">${url}</a>`
  }

  return (text, origin = window.location.origin) => markdown.render(text, { origin })
}

function isSameOrigin(src: string, origin: string): boolean {
  try {
    return new URL(src, origin + '/').origin === origin
  } catch {
    return false
  }
}

const chat = createMarkdownRenderer()

/** Renders model output as HTML: raw HTML escaped, links open in a new tab, only same-origin images. */
export function renderMarkdown(text: string, origin: string = window.location.origin): string {
  return chat(text, origin)
}
