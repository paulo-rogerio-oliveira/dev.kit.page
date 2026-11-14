import { expect, type APIRequestContext } from '@playwright/test';
import type { DocumentoMatrix, ResultadoMatrix } from '../src/api/matrix';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/** Só dados fictícios na base descartável iniciada pelo runner. Também reutilizável na recaptura. */
export async function prepararMatrix(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json();
  const criadaEmpresa = await request.post(`${API}/api/empresas`, { headers: { Authorization: `Bearer ${admin.token}` },
    data: { nome: 'Equipe fictícia Matrix', plano: 'Empresarial', assentos: 5 } });
  expect(criadaEmpresa.ok(), await criadaEmpresa.text()).toBeTruthy();
  const empresa = await criadaEmpresa.json();
  const conta = await request.post(`${API}/api/usuarios`, { headers: { Authorization: `Bearer ${admin.token}` },
    data: { login: `demonstracao-matrix-${Date.now()}`, nome: 'Demonstração', papel: 'dev', empresaId: empresa.id } });
  expect(conta.ok(), await conta.text()).toBeTruthy();
  const { usuario, senhaTemporaria } = await conta.json();
  const inicial = await (await request.post(`${API}/api/auth/login`, { data: { login: usuario.login, senha: senhaTemporaria } })).json();
  const troca = await request.post(`${API}/api/auth/trocar-senha`, { headers: { Authorization: `Bearer ${inicial.token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA } });
  expect(troca.ok()).toBeTruthy();
  const sessao = await troca.json(), headers = { Authorization: `Bearer ${sessao.token}`, 'X-DevKit-Matrix-Avaliacao': '1' };
  const repositorios = ['https://example.test/demo/aplicacao'];
  const envio = { slug: 'matrix', nome: 'matrix · demonstração', nivel: 'usuario', time: '', repositorios,
    branch: 'feature/demo/matrix', semanaConvertida: false, definicao: 'Matriz de impacto para demonstração isolada.',
    anexos: [], autonomia: true, paralelismo: 2, destinoDaPr: 'main', situacao: 'aprovado', versao: null,
    itens: [2, 3, 4].map((numero, i) => ({ numero, titulo: ['Contratos e distribuição', 'Editor Desktop', 'Editor Page'][i], descricao: '', fluxo: 'Tela',
      repositorios, componentes: [], dependencias: numero > 2 ? [2] : [], onda: i + 1, workItemId: 0, situacao: numero === 4 ? 'planejado' : 'integrado', branch: `feature/demo/matrix-item-${numero}` })) };
  const criada = await request.put(`${API}/api/projetos`, { headers, data: envio });
  expect(criada.status()).toBe(201); const projeto = await criada.json();
  const contaLeitor = await request.post(`${API}/api/usuarios`, { headers: { Authorization: `Bearer ${admin.token}` },
    data: { login: `leitor-matrix-${Date.now()}`, nome: 'Leitor de demonstração', papel: 'dev', empresaId: empresa.id } });
  expect(contaLeitor.ok(), await contaLeitor.text()).toBeTruthy();
  const leitorCriado = await contaLeitor.json();
  const entradaLeitor = await (await request.post(`${API}/api/auth/login`, { data: { login: leitorCriado.usuario.login, senha: leitorCriado.senhaTemporaria } })).json();
  const trocaLeitor = await request.post(`${API}/api/auth/trocar-senha`, { headers: { Authorization: `Bearer ${entradaLeitor.token}` },
    data: { senhaAtual: leitorCriado.senhaTemporaria, novaSenha: NOVA_SENHA } });
  expect(trocaLeitor.ok()).toBeTruthy(); const leitor = await trocaLeitor.json();
  const concessao = await request.post(`${API}/api/projetos/${projeto.id}/acessos`, { headers, data: { usuario: leitor.login, papel: 'leitor' } });
  expect(concessao.ok(), await concessao.text()).toBeTruthy();
  const componentes = ['Matriz de impacto', 'Avaliação', 'Distribuição às tasks', 'Histórico'].map((nome, i) => ({ id: `componente-${i}`, nome, orientacao: 'Ver orientação no mapa da aplicação' }));
  const layers = ['Desktop', 'Page', 'API', 'Serviço', 'devcli'].map((nome, i) => ({ id: `layer-${i}`, nome, orientacao: '' }));
  const mapa: DocumentoMatrix = { contrato: 1, aplicacao: 'demo', tipo: 'mapa', id: 'demo', projeto: 'matrix', mapa: {
    nome: 'dev.kit · demonstração', repositorios, componentes, layers,
    relacoes: [
      { id: 'rel-editor', origem: componentes[0].id, destino: componentes[1].id, layers: [layers[1].id, layers[2].id],
        gatilho: 'Alterar o editor ou o contrato de resultado', motivo: 'Manter o mesmo significado em todos os clientes.', propagacao: 'Revisar a edição e a avaliação por célula.', verificacao: 'Conferir definição, revisão e SHAs avaliados.' },
      { id: 'rel-distribuir', origem: componentes[0].id, destino: componentes[2].id, layers: [layers[2].id, layers[3].id],
        gatilho: 'Alterar a revisão do recorte', motivo: 'Distribuir apenas às tasks afetadas.', propagacao: 'Conferir a entrega do recorte.', verificacao: 'Preservar avaliações de outras células.' },
      { id: 'rel-historico', origem: componentes[0].id, destino: componentes[3].id, layers: [layers[1].id],
        gatilho: 'Alterar o histórico do editor', motivo: 'Permitir comparação das revisões também no celular.', propagacao: 'Revisar a apresentação do histórico.', verificacao: 'Conferir autor, revisão e evidências anteriores.' },
    ],
  } };
  async function gravar(documento: DocumentoMatrix, versaoEsperada: number): Promise<ResultadoMatrix> {
    const r = await request.put(`${API}/api/matrix/${documento.aplicacao}/${documento.tipo}/${documento.id}?aplicar=true`, { headers, data: { versaoEsperada, documento } });
    expect(r.ok(), await r.text()).toBeTruthy(); return r.json();
  }
  await gravar(mapa, 0); await gravar(mapa, 1);
  const matriz: DocumentoMatrix = { contrato: 1, aplicacao: 'demo', tipo: 'matriz', id: 'matrix', projeto: 'matrix', matriz: {
    revisaoDoMapa: 2, componentes, layers, celulas: componentes.flatMap((c, ci) => layers.map((l, li) => ({
      id: ci === 1 && li === 1 ? 'imp-008' : `imp-${ci}-${li}`, componente: c.id, layer: l.id,
      obrigacao: li < 2 ? 'alterar' as const : 'preservar' as const, aplicavel: !(ci === 2 && li < 2),
      justificativa: ci === 2 && li < 2 ? 'Entrega pelo serviço; consultar justificativa.' : [
        ['Editar componentes', 'Editar no celular', 'Persistir revisão', 'Distribuir recorte', 'Consultar matriz'],
        ['Resultado por célula', 'Avaliação e evidências', 'Vincular versão', 'Revisar afetadas', 'Registrar motivo'],
        ['Consultar justificativa', 'Consultar justificativa', 'Sincronizar revisão', 'Entregar recorte', 'Consultar recebido'],
        ['Comparar revisões', 'Histórico móvel', 'Preservar versões', 'Identificar tasks', 'Consultar histórico'],
      ][ci][li],
      origem: `orientação ${c.nome} · mapa r2`, responsaveis: [{ id: `item-${li === 0 ? 3 : li === 1 ? 4 : 2}`, item: li === 0 ? 3 : li === 1 ? 4 : 2 }],
      criterio: 'Conferir resultado, revisão avaliada e evidências no desktop e em 390 px.', evidenciaEsperada: 'Imagem da célula e roteiro de validação.',
    }))).filter(c => !(c.componente === componentes[3].id && c.layer === layers[1].id)) } };
  for (let v = 0; v < 4; v++) {
    const documento = structuredClone(matriz);
    if (v < 3) documento.matriz!.celulas.find(c => c.id === 'imp-008')!.criterio = 'Conferir resultado e evidências da célula.';
    await gravar(documento, v);
  }
  let versaoRegistro = 0;
  for (const c of matriz.matriz!.celulas.filter(c => c.layer !== 'layer-0' && c.layer !== 'layer-1')) {
    await gravar({ contrato: 1, aplicacao: 'demo', tipo: 'registro', id: 'matrix', projeto: 'matrix', registro: {
      celula: c.id, revisaoDaDefinicao: 4, responsavel: 'item-2', tipo: 'avaliacao', resultado: 'atendido', justificativa: 'Contrato preservado nos itens anteriores da demonstração.',
      evidencias: ['log-demonstracao.txt'], shas: { 'demo/aplicacao': 'a'.repeat(40) },
    } }, versaoRegistro++);
  }
  for (const tipo of ['implementacao', 'avaliacao'] as const) await gravar({ contrato: 1, aplicacao: 'demo', tipo: 'registro', id: 'matrix', projeto: 'matrix', registro: {
    celula: 'imp-008', revisaoDaDefinicao: 3, responsavel: 'item-4', tipo, resultado: 'parcial',
    justificativa: tipo === 'implementacao' ? 'Comprovação anterior de demonstração, preservada no histórico.' : 'Resultado anterior de demonstração: conferir a evidência da célula.',
    evidencias: ['captura-anterior-demonstracao.png'], shas: { 'demo/aplicacao': 'b'.repeat(40) },
  } }, versaoRegistro++);
  const maquina = 'matrix-demo', task = 'matrix-item-4';
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers, data: { contrato: 1, maquinaId: maquina, nome: 'Ambiente de demonstração', versaoDevKit: '146', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false } })).ok()).toBeTruthy();
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: { contrato: 1, maquinaId: maquina, geradoEm: new Date().toISOString(),
    projetos: [{ slug: 'matrix', nome: projeto.nome, situacao: 'aprovado', ondaAtual: 3, itens: projeto.itens }],
    fluxos: [{ id: task, nome: 'Item 4 · Editor Page da matriz', etapa: 'Implementação', situacao: 'rodando', projeto: 'matrix', item: 4, desdeEm: new Date().toISOString() }], pendencias: [],
  } })).ok()).toBeTruthy();
  await gravar({ contrato: 1, aplicacao: 'demo', tipo: 'vinculo', id: 'matrix', projeto: 'matrix', vinculo: { responsavel: 'item-4', sessao: task, item: 4, maquina } }, 0);
  return { sessao, headers, projeto, matriz, mapa, gravar, maquina, task, leitor };
}
