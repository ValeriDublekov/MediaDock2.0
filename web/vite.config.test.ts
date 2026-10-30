import { describe, expect, it } from 'vitest'
import viteConfig, { assertLoopbackHost } from './vite.config'

describe('Vite development server boundary', () => {
  it('binds to IPv4 loopback by default', () => {
    expect(viteConfig.server?.host).toBe('127.0.0.1')
  })

  it.each(['127.0.0.1', 'localhost', '::1'])('allows loopback host %s', (host) => {
    expect(() => assertLoopbackHost(host)).not.toThrow()
  })

  it.each([true, '0.0.0.0', '192.168.1.20'])('rejects non-loopback host %s', (host) => {
    expect(() => assertLoopbackHost(host)).toThrow('must bind to loopback')
  })
})