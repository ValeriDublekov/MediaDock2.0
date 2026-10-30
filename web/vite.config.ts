import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const loopbackHosts = new Set(['127.0.0.1', 'localhost', '::1'])

export function assertLoopbackHost(host: string | boolean | undefined) {
  if (typeof host === 'string' && loopbackHosts.has(host)) return
  throw new Error('The MediaDock development server must bind to loopback.')
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), {
    name: 'mediadock-loopback-only-server',
    configResolved(config) {
      if (config.command === 'serve') assertLoopbackHost(config.server.host)
    },
  }],
  server: {
    host: '127.0.0.1',
    proxy: {
      '/api': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
    },
  },
})
