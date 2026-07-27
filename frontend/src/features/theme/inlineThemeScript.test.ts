import { beforeEach, describe, expect, it } from 'vitest'
import indexHtml from '../../../index.html?raw'

// index.html carries a blocking inline script that sets the theme class before
// the first paint. These tests extract that script and run it against stubbed
// inputs, so the file also fails if the script is ever removed or made async.

const inlineScripts = [...indexHtml.matchAll(/<script(\s[^>]*)?>([\s\S]*?)<\/script>/g)]
const themeScript = inlineScripts.find(
  ([, attrs, body]) => !attrs && body.includes('theme'),
)

const THROWS = Symbol('localStorage throws')
type Stored = string | null | typeof THROWS

function runInlineScript(stored: Stored, prefersDark: boolean): boolean {
  document.documentElement.className = ''

  const localStorageStub = {
    getItem: (key: string) => {
      if (stored === THROWS) {
        throw new DOMException('access denied', 'SecurityError')
      }
      return key === 'theme' ? stored : null
    },
  }
  const windowStub = {
    matchMedia: (query: string) => ({
      matches: query.includes('prefers-color-scheme: dark') && prefersDark,
    }),
  }

  // `localStorage` and `window` are parameters, so they shadow the jsdom
  // globals inside the script body; `document` is the real jsdom document.
  new Function('localStorage', 'window', themeScript![2])(
    localStorageStub,
    windowStub,
  )

  return document.documentElement.classList.contains('dark')
}

describe('inline theme script in index.html', () => {
  beforeEach(() => {
    document.documentElement.className = ''
  })

  it('is present as a blocking script', () => {
    expect(themeScript).toBeDefined()
  })

  it('runs before the module entry point so the class lands before first paint', () => {
    const moduleEntry = indexHtml.indexOf('src="/src/main.tsx"')
    expect(moduleEntry).toBeGreaterThan(-1)
    expect(indexHtml.indexOf(themeScript![0])).toBeLessThan(moduleEntry)
  })

  it('applies dark for a stored dark preference regardless of the OS setting', () => {
    expect(runInlineScript('dark', false)).toBe(true)
    expect(runInlineScript('dark', true)).toBe(true)
  })

  it('applies light for a stored light preference regardless of the OS setting', () => {
    expect(runInlineScript('light', true)).toBe(false)
    expect(runInlineScript('light', false)).toBe(false)
  })

  it('follows the OS setting in system mode', () => {
    expect(runInlineScript('system', true)).toBe(true)
    expect(runInlineScript('system', false)).toBe(false)
  })

  it('defaults to system mode when nothing is stored', () => {
    expect(runInlineScript(null, true)).toBe(true)
    expect(runInlineScript(null, false)).toBe(false)
  })

  it('resolves a malformed stored value to light, matching useTheme', () => {
    // useTheme's isDark is false for any value that is neither 'dark' nor
    // 'system', so the script must agree or a hand-edited localStorage would
    // flash dark before React corrected it to light.
    expect(runInlineScript('purple', true)).toBe(false)
    expect(runInlineScript('', true)).toBe(false)
  })

  it('does not throw when localStorage is unavailable', () => {
    expect(() => runInlineScript(THROWS, true)).not.toThrow()
    expect(runInlineScript(THROWS, true)).toBe(false)
  })
})
