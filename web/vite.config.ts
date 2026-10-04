/// <reference types="vitest/config" />
import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * A URL pública do site nas meta tags (canonical, Open Graph): o og:image precisa ser absoluto. Vem
 * de VITE_SITE_URL no build (a variável do Static Web Apps); sem ela, os caminhos do Open Graph ficam
 * relativos e o canonical sai (um canonical relativo é inválido para os buscadores).
 */
function urlDoSite(url: string): Plugin {
  const base = url.replace(/\/$/, '');
  return {
    name: 'url-do-site',
    transformIndexHtml: (html) =>
      (base ? html : html.replace(/\s*<link rel="canonical"[^>]*>/, '')).replaceAll('__URL_DO_SITE__', base),
  };
}

// A base da API vem de VITE_API_URL (ver .env.example); os testes de componente não a usam —
// o MSW responde no lugar dela.
export default defineConfig(({ mode }) => ({
  plugins: [react(), urlDoSite(loadEnv(mode, process.cwd(), 'VITE_').VITE_SITE_URL ?? '')],
  server: { port: 5173 },
  preview: { port: 4173 },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/testes/setup.ts'],
    env: { VITE_API_URL: 'http://api.test' },
    include: ['src/**/*.test.{ts,tsx}'],
    reporters: ['default', 'junit'],
    outputFile: { junit: './test-results/vitest-junit.xml' },
  },
}));
