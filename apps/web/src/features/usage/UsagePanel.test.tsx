// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { UsagePanel } from './UsagePanel'
import { UsageStore } from './UsageStore'

afterEach(() => { cleanup(); vi.useRealTimers() })
it('labels expired and missing windows without inventing remaining quota', () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-09-08T00:00:00Z'))
  const store = new UsageStore(() => 'quota')
  store.setOnline(true)
  store.apply({ type: 'account.rateLimits.response', requestId: 'quota', timestamp: 0, payload: {
    fetchedAt: 1788825600,
    buckets: [
      { limitId: 'codex', limitName: null, primary: { usedPercent: 4, windowDurationMins: 300, resetsAt: 1 }, secondary: null },
      { limitId: 'extra', limitName: '额外额度', primary: { usedPercent: 100, windowDurationMins: 15, resetsAt: null }, secondary: null },
    ],
  } })
  render(<UsagePanel store={store} />)
  expect(screen.getByText('待刷新')).toBeTruthy()
  expect(screen.queryByText('96%')).toBeNull()
  expect(screen.getAllByText('暂无数据')).toHaveLength(2)
  expect(screen.getByText('0%')).toBeTruthy()
  expect(screen.getByText('15 分钟')).toBeTruthy()
  expect(screen.getByText('额外额度')).toBeTruthy()
  store.dispose()
})
