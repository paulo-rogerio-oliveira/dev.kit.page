import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { instalarNavegador, observadores, restaurarNavegador } from './navegador';
import { cadastros, downloads, escritas, gestaoDeUsuarios, pedidosRecebidos, reacoes, reiniciarRelatos, requisicoes, servidor } from './servidor';

// Com a suíte inteira em paralelo no jsdom, o findBy* passava do 1 s padrão em máquina carregada (o Dashboard e a
// Compra, com o topo e o painel da marca): 5 s de espera, e o teste segue falhando de verdade quando o elemento não vem.
configure({ asyncUtilTimeout: 5_000 });

instalarNavegador();

// As requisições que o servidor de mentira começou e ainda não respondeu. Um teste que termina no mesmo render
// em que os filhos montam (o Dashboard: o ROI e as exceções) deixa a consulta deles em voo; sem esperar por ela,
// o handler a registra em `requisicoes` DEPOIS da limpeza, e ela aparece no teste seguinte (o do dev, que espera nenhuma).
const emVoo = new Set<string>();
servidor.events.on('request:start', ({ requestId }) => { emVoo.add(requestId); });
servidor.events.on('request:end', ({ requestId }) => { emVoo.delete(requestId); });

/** Espera o servidor de mentira responder o que já começou (com teto, para um handler preso não travar a suíte). */
async function esperarAsRequisicoesEmVoo(tetoMs = 2_000) {
  const ate = Date.now() + tetoMs;
  do {
    await new Promise((pronto) => setTimeout(pronto, 0));
  } while (emVoo.size > 0 && Date.now() < ate);
  emVoo.clear();
}

beforeAll(() => servidor.listen({ onUnhandledRequest: 'error' }));
afterEach(async () => {
  cleanup();
  await esperarAsRequisicoesEmVoo();
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
