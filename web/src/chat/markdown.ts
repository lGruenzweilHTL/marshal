import MarkdownIt from 'markdown-it'

const markdown = new MarkdownIt({ html: false, linkify: false })

const defaultLink = markdown.renderer.rules.link_open ?? ((tokens, index, options, _env, self) => self.renderToken(tokens, index, options))
markdown.renderer.rules.link_open = (tokens, index, options, env, self) => {
  tokens[index]!.attrSet('rel', 'noopener noreferrer')
  tokens[index]!.attrSet('target', '_blank')
  return defaultLink(tokens, index, options, env, self)
}

/**
 * Images load only from the app's own origin. Anything else (another host, protocol-relative, `data:`) becomes a link
 * showing its URL, so model output cannot make the browser send data to a third party.
 */
markdown.renderer.rules.image = (tokens, index, options, env, self) => {
  const token = tokens[index]!
  const src = String(token.attrGet('src') ?? '')
  if (typeof env?.origin === 'string' && isSameOrigin(src, env.origin)) {
    token.attrSet('alt', self.renderInlineAsText(token.children ?? [], options, env))
    return self.renderToken(tokens, index, options)
  }
  const url = markdown.utils.escapeHtml(src)
  return `<a href="${url}" rel="noopener noreferrer" target="_blank">${url}</a>`
}

function isSameOrigin(src: string, origin: string): boolean {
  try {
    return new URL(src, origin + '/').origin === origin
  } catch {
    return false
  }
}

/** Renders model output as HTML: raw HTML escaped, links open in a new tab, only same-origin images. */
export function renderMarkdown(text: string, origin: string = window.location.origin): string {
  return markdown.render(text, { origin })
}
