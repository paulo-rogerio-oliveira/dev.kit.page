import { defineConfig } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import base from './playwright.config';

// Somente o cliente Matrix e sua API real; não executa os demais cenários do repositório.
export default defineConfig({ ...base, testMatch: 'matrix.spec.ts',
  outputDir: join(tmpdir(), `devkit-matrix-${process.pid}`),
  reporter: [['list'], ['junit', { outputFile: `test-results/matrix-${process.pid}/junit.xml` }]],
  projects: [{ name: 'matrix', testMatch: 'matrix.spec.ts' }],
  use: { ...base.use, channel: 'msedge', actionTimeout: 15_000 },
});
