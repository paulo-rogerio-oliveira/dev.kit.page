import { defineConfig } from '@playwright/test';
import base from './playwright.config';

/** API real e SQLite novo do runner; só o percurso humano, sem publicar ou usar uma base compartilhada. */
export default defineConfig({
  ...base,
  projects: undefined,
  testMatch: 'ciclo-item.e2e.ts',
  timeout: 300_000,
  reporter: [['list'], ['junit', { outputFile: 'test-results/ciclo-item-junit.xml' }]],
  use: { ...base.use, channel: process.env.DEVKIT_E2E_BROWSER_CHANNEL ?? 'msedge',
    trace: 'off', video: 'off', screenshot: 'off', actionTimeout: 20_000 },
});
