import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// /api va /health so'rovlari IdM API'ga proksi qilinadi: brauzer uchun bitta manba, CORS kerak emas.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:7100',
      '/health': 'http://localhost:7100',
    },
  },
  test: { environment: 'node' },
})
