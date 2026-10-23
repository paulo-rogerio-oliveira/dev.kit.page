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
  // O fluxo.spec é o cenário da BASE NOVA (o admin com a senha inicial e nenhuma máquina): roda primeiro, e os
  // demais — que trocam a senha do admin pela API (admin.ts) e gravam dados — depois dele. Sem isso a ordem era a
  // alfabética, e um cenário com nome anterior (o conceitos.spec) já encontrava a senha trocada.
  projects: [
    { name: 'base-nova', testMatch: 'fluxo.spec.ts' },
    { name: 'cenarios', testIgnore: 'fluxo.spec.ts', dependencies: ['base-nova'] },
  ],
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
        // Os cenários dividem o MESMO admin, em sequência e dentro do mesmo minuto, e cada um cria empresa,
        // gestor ou usuário — escritas da política Exportacao (10 por minuto por usuário): somadas, a de quem
        // roda por último recebia 429. O limite em si é coberto pelo teste da API (Exportacao_em_laco_e_429).
        Painel__ExportacoesPorMinuto: '100',
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
