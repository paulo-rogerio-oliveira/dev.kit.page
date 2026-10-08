import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { instalarNavegador, observadores, restaurarNavegador } from './navegador';
import { cadastros, pedidosRecebidos, reacoes, requisicoes, servidor } from './servidor';

instalarNavegador();

beforeAll(() => servidor.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  cleanup();
  servidor.resetHandlers();
  sessionStorage.clear();
  requisicoes.length = 0;
  pedidosRecebidos.length = 0;
  reacoes.length = 0;
  cadastros.empresas.length = 0;
  cadastros.gestores.length = 0;
  observadores.length = 0;
  restaurarNavegador();
});
afterAll(() => servidor.close());
