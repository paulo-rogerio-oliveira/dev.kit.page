/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// A base da API vem de VITE_API_URL (ver .env.example); os testes de componente não a usam —
// o MSW responde no lugar dela.
export default defineConfig({
  plugins: [react()],
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
});
