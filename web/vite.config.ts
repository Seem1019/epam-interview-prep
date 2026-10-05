import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on :5180; Vite proxies /api so the browser never talks to Anthropic directly.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: 'http://localhost:5180', changeOrigin: true },
    },
  },
})
