import { defineConfig } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { CODIGO_DE_REGISTRO, PORTA_DA_API, PORTA_DA_WEB, SENHA_INICIAL } from './e2e/ambiente';

// Uma base SQLite NOVA a cada execução: o cenário começa do zero (o admin é semeado na subida).
const banco = join(tmpdir(), `devkitpage-e2e-${process.pid}-${Date.now()}.db`);

export default defineConfig({
  testDir: './e2e',
  timeout: 120_000,
  workers: 1,
  reporter: [['list'], ['junit', { outputFile: 'test-results/e2e-junit.xml' }], ['html', { open: 'never' }]],
  use: {
    baseURL: `http://localhost:${PORTA_DA_WEB}`,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      // A API de verdade, com a configuração por ambiente — o mesmo caminho do Azure.
      command: 'dotnet run --project ../api/src/DevKitPage.Api --no-launch-profile',
      url: `http://localhost:${PORTA_DA_API}/health`,
      timeout: 300_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: `http://localhost:${PORTA_DA_API}`,
        ConnectionStrings__DevKitPage: `Data Source=${banco}`,
        Seed__AdminPassword: SENHA_INICIAL,
        Telemetria__CodigoDeRegistro: CODIGO_DE_REGISTRO,
        Cors__Origens__0: `http://localhost:${PORTA_DA_WEB}`,
      },
    },
    {
      // A web compilada (o que vai para produção), apontando para a API acima.
      command: `npx vite build && npx vite preview --port ${PORTA_DA_WEB} --strictPort`,
      url: `http://localhost:${PORTA_DA_WEB}`,
      timeout: 180_000,
      reuseExistingServer: false,
      env: { VITE_API_URL: `http://localhost:${PORTA_DA_API}` },
    },
  ],
});
