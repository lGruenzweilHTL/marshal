import { readFileSync } from 'node:fs'
import { Resvg } from '@resvg/resvg-js'
import type { Plugin } from 'vite'

const ui = new URL('../src/ui/', import.meta.url)
const offline = new URL('../src/offline/', import.meta.url)
const read = (base: URL, path: string) => readFileSync(new URL(path, base), 'utf8')
const withoutComments = (svg: string) => svg.replace(/<!--[\s\S]*?-->\s*/g, '')

function png(svg: string, size: number): Buffer {
  return new Resvg(svg, { fitTo: { mode: 'width', value: size } }).render().asPng()
}

/** PWA icon sizes rendered from the logo's icon SVGs (see src/ui/README.md, "Replacing the logo"). */
export const icons = [
  { src: 'icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
  { src: 'icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
  { src: 'icons/maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
]

/**
 * Emits the app icons (PNG from `ui/logo/icon*.svg`, plus the SVG favicon and the Apple touch icon) and the static
 * offline page (`offline.html` with its stylesheet built from `tokens.css`), so the logo and colours have one source.
 */
export function appAssets(): Plugin {
  return {
    name: 'marshal-app-assets',
    apply: 'build',
    generateBundle() {
      const icon = withoutComments(read(ui, 'logo/icon.svg'))
      const maskable = withoutComments(read(ui, 'logo/icon-maskable.svg'))
      const emit = (fileName: string, source: string | Uint8Array) => this.emitFile({ type: 'asset', fileName, source })
      emit('favicon.svg', icon)
      emit('icons/icon-192.png', png(icon, 192))
      emit('icons/icon-512.png', png(icon, 512))
      emit('icons/maskable-512.png', png(maskable, 512))
      emit('icons/apple-touch-icon.png', png(maskable, 180))

      const wordmark = withoutComments(read(ui, 'logo/wordmark.svg')).replace('<svg ', '<svg aria-hidden="true" ')
      const wifiOff =
        '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h.01M8.5 16.43a5 5 0 0 1 7 0M2 8.82a15 15 0 0 1 4.17-2.65M10.66 5c4.01-.36 8.14.9 11.34 3.76M16.85 11.25a10 10 0 0 1 2.22 1.68M5 13a10 10 0 0 1 5.24-2.76M2 2l20 20"/></svg>'
      emit('offline.html', read(offline, 'offline.html').replace('<!--wordmark-->', wordmark).replace('<!--wifi-off-->', wifiOff))
      // The offline page uses system fonts: the font files are content-hashed and the page must not depend on them.
      emit('offline.css', read(ui, 'tokens.css') + read(offline, 'offline.css'))
      emit('offline.js', read(offline, 'offline.js'))
    },
  }
}
