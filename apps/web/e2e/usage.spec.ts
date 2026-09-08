import { expect, test } from '@playwright/test'

test('phone shows real quota response, refreshes, and clears offline values', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => localStorage.setItem('codex-companion.web-credential.v1', JSON.stringify({ deviceId: 'fixture', credential: 'test-only' })))
  let requests = 0
  let offline = () => {}
  await page.routeWebSocket('**/ws/web', (socket) => {
    offline = () => socket.send(JSON.stringify({ type: 'device.offline', payload: {}, timestamp: Date.now() }))
    socket.onMessage((raw) => {
      const request = JSON.parse(String(raw))
      const send = (type: string, payload: unknown) => socket.send(JSON.stringify({ type, payload, requestId: request.requestId, timestamp: Date.now() }))
      if (request.type === 'device.hello') send('device.online', {})
      if (request.type === 'thread.list.request') send('thread.list.response', { threads: [] })
      if (request.type === 'account.rateLimits.request') {
        requests++
        const now = Math.floor(Date.now() / 1000)
        send('account.rateLimits.response', { fetchedAt: now, buckets: [{ limitId: 'codex', limitName: null, primary: { usedPercent: requests === 1 ? 4 : 5, windowDurationMins: 300, resetsAt: now + 3600 }, secondary: { usedPercent: 16, windowDurationMins: 10080, resetsAt: now + 86400 * 6 } }] })
      }
    })
  })
  await page.goto('/')
  await page.getByRole('button', { name: '打开会话列表' }).click()
  const panel = page.getByRole('region', { name: '剩余用量' })
  await expect(panel.getByText('96%', { exact: true })).toBeVisible()
  await expect(panel.getByText('84%', { exact: true })).toBeVisible()
  await expect(panel.getByText('5 小时', { exact: true })).toBeVisible()
  await expect(panel.getByText('1 周', { exact: true })).toBeVisible()
  await expect(panel.locator('time')).toHaveCount(2)
  await expect.poll(async () => (await page.locator('.sidebar').boundingBox())!.x).toBe(0)
  await page.screenshot({ path: 'test-results/usage-phone.png' })
  await page.getByRole('button', { name: '刷新额度' }).click()
  await expect(panel.getByText('95%', { exact: true })).toBeVisible()
  offline()
  await expect(panel.getByText('电脑离线，连接后显示额度')).toBeVisible()
  await expect(panel.getByText('95%', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '刷新额度' })).toBeDisabled()
})
