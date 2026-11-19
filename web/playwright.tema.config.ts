import { defineConfig } from '@playwright/test';
import base from './playwright.config';
import { join } from 'node:path';

// Tema global e consumidores com paleta própria. API e SQLite novos, sem usuário de produção.
const cenarios = ['tema-claro.spec.ts', 'matrix.spec.ts', 'matrix-retomada.spec.ts', 'matrix-avaliacao.spec.ts'];
export default defineConfig({
  ...base,
  testMatch: cenarios,
  projects: [{ name: 'tema-claro', testMatch: cenarios }],
  outputDir: join('test-results', `tema-claro-${Date.now()}`),
  use: { ...base.use, channel: 'msedge', actionTimeout: 15_000, trace: 'off' },
  reporter: [['list'], ['junit', { outputFile: 'test-results/tema-claro/junit.xml' }]],
});
