import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    // Pinned, not incidental. The Entra registration lists http://localhost:5173/ as a
    // redirect URI, and Vite's habit of falling forward to 5174 when the port is busy
    // would produce a redirect_uri mismatch that looks like an auth bug.
    port: 5173,
    strictPort: true,
  },
})
