import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Builds into the server's wwwroot so one container serves the API and the SPA from the same origin.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../src/HearthGate.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
      '/icons': 'http://localhost:5080',
      '/signin-oidc': 'http://localhost:5080',
    },
  },
})
