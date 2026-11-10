import { defineConfig } from '@playwright/test';
import evidencias from './playwright.evidencias.config';

/**
 * (semana 7) As evidências dos itens 2 e 4 (`evidencias-semana7.e2e.ts`): o MESMO ambiente do roteiro do controle-v4 — a API de
 * verdade com SQLite temporário, a fila simulada, o worker da provisão com o provedor simulado e a web compilada —, só com o
 * roteiro desta semana. Sem trace, vídeo nem relatório HTML, como o de lá.
 */
export default defineConfig({ ...evidencias, testMatch: 'evidencias-semana7.e2e.ts' });
