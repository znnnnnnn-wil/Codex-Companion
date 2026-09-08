import { expect, test } from '@playwright/test'

test('detects a deployment without reloading or discarding the browser pairing', async ({ page }) => {
  const credential = JSON.stringify({ deviceId: 'fixture', credential: 'test-only' })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('**/version.json', route => route.fulfill({ json: { version: 'next-test-release' } }))
  await page.routeWebSocket('**/ws/web', () => {})
  await page.goto('/')
  await page.evaluate((value) => localStorage.setItem('codex-companion.web-credential.v1', value), credential)
  await page.reload()
  await page.getByRole('button', { name: '打开会话列表' }).click()
  await expect(page.getByRole('button', { name: '发现新版，刷新页面' })).toBeVisible()
  expect(new URL(page.url()).search).toBe('')
  expect(await page.evaluate(() => localStorage.getItem('codex-companion.web-credential.v1'))).toBe(credential)
  await page.getByRole('button', { name: '发现新版，刷新页面' }).click()
  await expect(page).toHaveURL(/web-version=next-test-release/)
  expect(await page.evaluate(() => localStorage.getItem('codex-companion.web-credential.v1'))).toBe(credential)
})
