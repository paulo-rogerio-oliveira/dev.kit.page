import { expect, type APIRequestContext } from '@playwright/test';
import { API, CODIGO_DE_REGISTRO } from './ambiente';

/** O painel com dados próprios também quando o spec de tema roda isolado. Tudo na API/SQLite descartáveis. */
export async function prepararPainelComDados(request: APIRequestContext) {
  const id = `tema-painel-${Date.now()}`;
  const registro = await request.post(`${API}/api/maquinas/registrar`, {
    data: { maquinaId: id, versaoDevKit: '1.4.0', codigoRegistro: CODIGO_DE_REGISTRO },
  });
  expect(registro.ok(), await registro.text()).toBeTruthy();
  const { chave } = await registro.json();
  const em = new Date().toISOString(), dia = em.slice(0, 10);
  const evento = (numero: number, tipo: string, valor: number | null, detalhe: string) => ({
    eventId: `${id}-${numero}`, tipo, sessaoId: id, quantidade: 1, valor, detalhe, em,
  });
  const excecao = 'System.InvalidOperationException';
  const envio = await request.post(`${API}/api/telemetria/lote`, {
    headers: { 'X-Machine-Key': chave },
    data: { versao: 'v1', maquinaId: id, versaoDevKit: '1.4.0', eventos: [
      evento(1, 'TurnoExecutado', 30000, 'codex'),
      evento(2, 'TurnoFalhou', null, 'nao-classificada'),
      { ...evento(3, 'ExcecaoNaoClassificada', null, id), assinatura: id, trace: `${excecao}: Demonstração\n   at Demo.Executar()` },
      { ...evento(4, 'RoiCalculado', 3, '387'), sessaoId: '', roi: { workItem: 387, tipo: 'User Story', estado: 'Closed',
        de: dia, ate: dia, turnosDoAgente: 3, sessoes: 1, horas: 2, horasNoBoard: 2, horasNoTimesheet: null,
        leadTimeDias: 1, aberto: false, pullRequests: 1, pullRequestsMergeadas: 1 } },
    ] },
  });
  expect(envio.status(), await envio.text()).toBe(202);
  const pedido = await request.post(`${API}/api/demonstracoes`, { data: {
    nome: 'Demonstração do tema', email: 'tema@example.test', empresa: 'Empresa fictícia',
    mensagem: 'Dados fictícios para conferir a tabela no celular.', consentimento: true,
  } });
  expect(pedido.ok(), await pedido.text()).toBeTruthy();
  return { excecao };
}
