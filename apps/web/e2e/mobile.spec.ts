import { expect, test } from '@playwright/test'

for (const width of [320, 390, 720, 1280]) {
  test(`chat content fits a ${width}px viewport`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 })
    await page.addInitScript(() => localStorage.setItem('codex-companion.web-credential.v1', JSON.stringify({ deviceId: 'fixture', credential: 'test-only' })))
    const thread = { threadId: 'long-title', title: '请检查并修复当前仓库 znnnnnnn-wil/Codex-Companion 中 Bridge 启动、认证、配对恢复和模式 A 部署流程存在的问题。'.repeat(3), cwd: 'E:\\codexDestop', updatedAt: Date.now(), status: 'idle', source: 'desktop' }
    await page.routeWebSocket('**/ws/web', (socket) => {
      socket.onMessage((data) => {
        const request = JSON.parse(String(data))
        const send = (type: string, payload: unknown) => socket.send(JSON.stringify({ type, payload, requestId: request.requestId, threadId: request.threadId, timestamp: Date.now() }))
        if (request.type === 'device.hello') send('device.online', {})
        if (request.type === 'thread.list.request') send('thread.list.response', { threads: [thread] })
        if (request.type === 'thread.read.request') send('thread.read.response', { threadId: thread.threadId, items: [{ id: 'message', type: 'message', role: 'assistant', content: '已完成，修复已进入 main，v0.1.6 正式版已发布。'.repeat(12) + '\n\nhttps://example.com/' + 'long'.repeat(100) + '\n\n```\n' + 'code'.repeat(100) + '\n```\n\n| 项目 | 说明 |\n| --- | --- |\n| 长内容 | ' + 'table'.repeat(100) + ' |', turnId: 'turn' }, { id: 'reply', type: 'message', role: 'user', rawType: 'userMessage', content: '<send_user_message_question_reply>' + JSON.stringify([{ questionItemId: 'request_user_input_async'.repeat(80), question: '请在手机完成上述配对后告知结果，我会核对 Bridge 在线状态和额度读取。', answer: '已完成配对' }]) + '</send_user_message_question_reply>', turnId: 'reply-turn' }] })
      })
    })
    await page.goto('/')
    if (width <= 720) await page.getByRole('button', { name: '打开会话列表' }).click()
    await page.locator('.thread-group-toggle').click()
    await page.locator('.thread-row').click()
    if (width <= 720) await expect.poll(async () => {
      const box = await page.locator('.sidebar').boundingBox()
      return box!.x + box!.width
    }).toBeLessThanOrEqual(0)
    await expect(page.locator('.message-bubble').first()).toBeVisible()
    // Check the children, not just document scrollWidth: the shell clips overflow.
    for (const selector of ['.chat-toolbar', '.message-scroll', '.message-bubble', '.composer', '.composer textarea', '.composer button:last-child']) {
      for (const element of await page.locator(selector).all()) {
        const box = await element.boundingBox()
        expect(box, selector).not.toBeNull()
        expect(box!.x, selector).toBeGreaterThanOrEqual(0)
        expect(box!.x + box!.width, selector).toBeLessThanOrEqual(width + 1)
      }
    }
    const scroll = page.locator('.message-scroll')
    expect(await scroll.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1)
    await page.screenshot({ path: `test-results/chat-${width}.png` })
  })
}
