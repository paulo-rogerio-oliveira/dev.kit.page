import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { instalarNavegador, observadores, restaurarNavegador } from './navegador';
import { cadastros, downloads, escritas, gestaoDeUsuarios, pedidosRecebidos, reacoes, reiniciarRelatos, requisicoes, servidor } from './servidor';

// Com a suíte inteira em paralelo no jsdom, o findBy* passava do 1 s padrão em máquina carregada (o Dashboard e a
// Compra, com o topo e o painel da marca): 5 s de espera, e o teste segue falhando de verdade quando o elemento não vem.
configure({ asyncUtilTimeout: 5_000 });

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
  gestaoDeUsuarios.length = 0;
  downloads.length = 0;
  escritas.length = 0;
  observadores.length = 0;
  reiniciarRelatos();
  restaurarNavegador();
});
afterAll(() => servidor.close());
