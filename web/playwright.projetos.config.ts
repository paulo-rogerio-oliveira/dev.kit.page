import { defineConfig } from '@playwright/test';
import base from './playwright.config';
import { PORTA_DA_WEB } from './e2e/ambiente';

export default defineConfig({ ...base, testMatch: 'projetos-ondas.spec.ts',
  projects: [{ name: 'projetos', testMatch: 'projetos-ondas.spec.ts' }],
  use: { ...base.use, baseURL: `http://127.0.0.1:${PORTA_DA_WEB}`, channel: 'msedge' },
  reporter: [['list'], ['junit', { outputFile: 'test-results/projetos-junit.xml' }]],
  webServer: [
    { ...base.webServer[0], command: base.webServer[0].command + ' --no-build' },
    { command: 'node e2e/servidor-projetos.mjs', url: `http://127.0.0.1:${PORTA_DA_WEB}`, timeout: 30_000,
      reuseExistingServer: false, env: { DEVKIT_DEMO_PROJETOS: '1' } },
  ],
});
