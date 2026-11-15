import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const css = await readFile(new URL('../src/css/custom.css', import.meta.url), 'utf8')
const palettes = [...css.matchAll(/\{([^{}]*--alpha-background:[^{}]*)\}/g)].map(match => Object.fromEntries([...match[1].matchAll(/--(alpha-[\w-]+):\s*(#[\da-f]{6})/gi)].map(value => [value[1], value[2]])))

function luminance(hex) {
  const channels = hex.slice(1).match(/../g).map(value => parseInt(value, 16) / 255).map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4)
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
}

test('alpha notices and badges exceed WCAG AAA text contrast in both themes', () => {
  assert.equal(palettes.length, 2)
  for (const palette of palettes) {
    for (const [foreground, background] of [['alpha-text', 'alpha-background'], ['alpha-badge-text', 'alpha-badge-background']]) {
      const values = [luminance(palette[foreground]), luminance(palette[background])].sort((a, b) => b - a)
      const ratio = (values[0] + 0.05) / (values[1] + 0.05)
      assert.ok(ratio >= 7, `${foreground} contrast ${ratio.toFixed(2)}:1 must reach 7:1`)
    }
  }
})
