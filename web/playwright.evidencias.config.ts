import { defineConfig } from '@playwright/test';
import base from './playwright.config';

/**
 * (controle-v4, item 5) As EVIDÊNCIAS ponta a ponta do board remoto: o mesmo ambiente do playwright.config.ts (a API de
 * verdade com SQLite temporário e a web compilada) mais o worker da provisão com o provedor simulado e a fila simulada na
 * API — sem eles o "Gerar artefato" e o ↻ do catálogo não chegam à máquina (409 sem-fila). Só o roteiro de evidências roda
 * aqui (`evidencias-controle-v4.e2e.ts`, fora do padrão *.spec.ts da suíte comum).
 * SEM trace, vídeo nem relatório HTML: o roteiro digita a senha do usuário de teste, e nada do que ela toca pode ficar gravado.
 */
type Servidor = Exclude<NonNullable<typeof base.webServer>, unknown[]> & { env: Record<string, string> };
const [api, web] = base.webServer as Servidor[];
const banco = api.env.ConnectionStrings__DevKitPage;

export default defineConfig({
  ...base,
  projects: undefined,
  testMatch: 'evidencias-controle-v4.e2e.ts',
  timeout: 600_000,
  reporter: [['list']],
  // Com teto por ação: um clique num elemento que sumiu (o aviso que fecha sozinho) não pendura o roteiro inteiro.
  use: { ...base.use, screenshot: 'off', trace: 'off', video: 'off', actionTimeout: 30_000 },
  webServer: [
    // Os zips dos artefatos numa pasta temporária, como as imagens do design: nada do cenário fica no clone.
    { ...api, env: { ...api.env, Remoto__Fila: 'simulado', Remoto__IntervaloDoSseSegundos: '1', Artefatos__Pasta: `${api.env.Design__Pasta}-artefatos` } },
    {
      // O worker da provisão na MESMA base: conclui a fila do controle remoto que a liberação do admin pede.
      command: 'dotnet run --project ../api/src/DevKitPage.Provisao --no-launch-profile',
      env: { DOTNET_ENVIRONMENT: 'Development', Provisao__Provedor: 'simulado', Provisao__SimulacaoSegundos: '1', Provisao__IntervaloSegundos: '2',
        ConnectionStrings__DevKitPage: banco },
      timeout: 300_000,
    },
    web,
  ],
});
