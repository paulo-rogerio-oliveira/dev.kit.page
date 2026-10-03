// O ambiente do cenário ponta a ponta — compartilhado entre o playwright.config.ts (que sobe a API
// e a web) e o teste. Nada aqui é segredo de verdade: é a base descartável do teste.
export const PORTA_DA_API = 5199;
export const PORTA_DA_WEB = 4199;
export const API = `http://localhost:${PORTA_DA_API}`;
export const SENHA_INICIAL = 'senhaInicialE2e2026';
export const NOVA_SENHA = 'senhaNovaE2e2026';
export const CODIGO_DE_REGISTRO = 'convite-e2e';
