import { defineConfig } from '@playwright/test';
import base from './playwright.config';

// Somente os trajetos alterados; API e SQLite descartáveis da configuração existente.
export default defineConfig({
  ...base,
  projects: [{ name: 'navegacao', testMatch: 'navegacao.spec.ts' }],
  use: { ...base.use, channel: 'msedge', actionTimeout: 15_000 },
  reporter: [['list'], ['junit', { outputFile: 'test-results/navegacao-junit.xml' }]],
});
