import { defineConfig } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import base from './playwright.config';

// Somente o cliente Matrix e sua API real; não executa os demais cenários do repositório.
export default defineConfig({ ...base, testMatch: 'matrix.spec.ts',
  // A recaptura pode manter a DLL da API carregada. Sem alteração na API, o runner pode usar
  // o binário já compilado, mantendo servidor e SQLite novos em cada execução.
  webServer: base.webServer.map(s => process.env.DEVKIT_E2E_API_NO_BUILD === '1' && s.command.startsWith('dotnet run')
    ? { ...s, command: s.command + ' --no-build' } : s),
  outputDir: join(tmpdir(), `devkit-matrix-${process.pid}`),
  reporter: [['list'], ['junit', { outputFile: `test-results/matrix-${process.pid}/junit.xml` }]],
  projects: [{ name: 'matrix', testMatch: 'matrix.spec.ts' }],
  use: { ...base.use, channel: 'msedge', actionTimeout: 15_000 },
});
