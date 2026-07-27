// @vitest-environment node
// (the suite default is jsdom, where import.meta.url is an http:// stub that
// fileURLToPath rejects)
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

// Archivo is the design system's only typeface and this app is meant to run on a
// LAN with no internet. These read the shipped files rather than a rendered
// component: the failure mode being guarded against is a CDN <link> creeping
// back in, which no component test would notice.

const fromFrontendRoot = (relative: string) =>
  fileURLToPath(new URL(`../../${relative}`, import.meta.url))

const read = (relative: string) => readFileSync(fromFrontendRoot(relative), 'utf8')

const FONT_PATH = '/fonts/archivo-latin-variable.woff2'

describe('self-hosted Archivo', () => {
  it('loads no fonts from a third-party CDN', () => {
    for (const file of ['index.html', 'src/index.css']) {
      expect(read(file)).not.toMatch(/fonts\.(googleapis|gstatic)\.com/)
    }
  })

  it('declares the local font with the full weight axis', () => {
    const css = read('src/index.css')

    expect(css).toContain('@font-face')
    expect(css).toContain(`url("${FONT_PATH}")`)
    expect(css).toMatch(/font-family:\s*"Archivo"/)
    // The vendored file is variable; anything narrower would clamp the weights
    // the app actually uses (500/600/700/800).
    expect(css).toMatch(/font-weight:\s*100 900/)
    expect(css).toMatch(/font-display:\s*swap/)
  })

  it('preloads the same file it declares', () => {
    const html = read('index.html')
    const preloadTag = html.match(/<link\b[^>]*\brel="preload"[^>]*>/)?.[0]

    // Asserted against the one tag rather than the whole document, so an
    // unrelated preload elsewhere can't satisfy these.
    expect(preloadTag).toBeDefined()
    expect(preloadTag).toContain(`href="${FONT_PATH}"`)
    expect(preloadTag).toContain('as="font"')
    expect(preloadTag).toContain('type="font/woff2"')
    // Font fetches are CORS-mode even same-origin; without this the preload is
    // discarded and the font fetched a second time.
    expect(preloadTag).toContain('crossorigin')
  })

  it('ships the font and its licence', () => {
    const font = readFileSync(fromFrontendRoot(`public${FONT_PATH}`))

    expect(font.subarray(0, 4).toString('ascii')).toBe('wOF2')
    expect(read('public/fonts/OFL.txt')).toContain('SIL Open Font License')
  })
})
