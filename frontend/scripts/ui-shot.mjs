#!/usr/bin/env node
// Drive a running Dollars2 dev server with Playwright and capture screenshots.
//
// Usage (start `npm run dev` first, then):
//   node scripts/ui-shot.mjs --url http://localhost:5173/login --out .ui-shots/login
//   node scripts/ui-shot.mjs --url http://localhost:5173/login --themes light,dark
//   node scripts/ui-shot.mjs --url http://localhost:5173/ --seed '{"token":"dev"}' --themes dark
//
// Flags:
//   --url <url>         Route to capture (required).
//   --out <prefix>      Output path prefix; files are <prefix>-<theme>.png
//                       (default: .ui-shots/shot).
//   --themes <list>     Comma-separated app themes to capture: light, dark,
//                       system (default: light,dark). Each is written to the
//                       app's own `theme` localStorage key so useTheme applies
//                       .dark exactly as in production.
//   --seed <json>       Extra localStorage entries to set before load, e.g. an
//                       auth token for authenticated routes.
//   --full              Full-page screenshot instead of viewport.
//   --wait <ms>         Extra settle time after load (default: 500).
//
// Exits non-zero if any page logs a console error or fails to load.
import { chromium } from 'playwright'
import { mkdir } from 'node:fs/promises'
import { dirname } from 'node:path'

function parseArgs(argv) {
  const args = {}
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a.startsWith('--')) {
      const key = a.slice(2)
      const next = argv[i + 1]
      if (next === undefined || next.startsWith('--')) {
        args[key] = true
      } else {
        args[key] = next
        i++
      }
    }
  }
  return args
}

const args = parseArgs(process.argv.slice(2))
if (!args.url) {
  console.error('error: --url is required')
  process.exit(2)
}

const url = args.url
const outPrefix = args.out || '.ui-shots/shot'
const themes = String(args.themes || 'light,dark')
  .split(',')
  .map((t) => t.trim())
  .filter(Boolean)
const seed = args.seed ? JSON.parse(args.seed) : {}
const fullPage = Boolean(args.full)
const settle = Number(args.wait ?? 500)

await mkdir(dirname(outPrefix) || '.', { recursive: true })

const browser = await chromium.launch()
let hadError = false
try {
  for (const theme of themes) {
    const context = await browser.newContext({ viewport: { width: 1200, height: 900 } })
    const page = await context.newPage()
    const consoleErrors = []
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text())
      }
    })
    page.on('pageerror', (err) => consoleErrors.push(String(err)))

    // First load establishes the origin so localStorage is writable.
    await page.goto(url, { waitUntil: 'load' })
    await page.evaluate(
      ([theme, seed]) => {
        localStorage.setItem('theme', theme)
        for (const [k, v] of Object.entries(seed)) {
          localStorage.setItem(k, String(v))
        }
      },
      [theme, seed],
    )
    await page.reload({ waitUntil: 'load' })
    await page.waitForTimeout(settle)

    const out = `${outPrefix}-${theme}.png`
    await page.screenshot({ path: out, fullPage })
    const applied = await page.evaluate(() =>
      document.documentElement.classList.contains('dark') ? 'dark' : 'light',
    )
    console.log(`captured ${out} (theme=${theme}, html=${applied})`)
    if (consoleErrors.length) {
      hadError = true
      console.error(`  console errors on ${theme}:`)
      for (const e of consoleErrors) {
        console.error(`    ${e}`)
      }
    }
    await context.close()
  }
} finally {
  await browser.close()
}

process.exit(hadError ? 1 : 0)
