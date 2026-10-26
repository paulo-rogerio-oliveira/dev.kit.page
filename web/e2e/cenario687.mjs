// O CENÁRIO da US #687 — o admin orientado a utilidade —, montado só pela API: as empresas (com os dados básicos e o
// contrato), os usuários com a licença definida pelo admin, a telemetria do painel de utilidade (a saúde de agora, o
// resultado, os turnos por desfecho e onde as tasks travam) e o catálogo básico publicado, com um rascunho.
//
// Dois usos: o e2e (us687.spec.ts) o importa; e, pela linha de comando, ele semeia uma API local para a RECAPTURA das
// telas no projeto de design (node e2e/cenario687.mjs <url da API> <senha do admin> <código de registro>).

const DIA = 86_400_000;

/** AAAA-MM-DD de hoje + dias (UTC, como a API conta). */
export const diaIso = (dias = 0) => new Date(Date.now() + dias * DIA).toISOString().slice(0, 10);

async function chamar(api, metodo, caminho, corpo, token, cabecalhos = {}) {
  const resposta = await fetch(`${api}${caminho}`, {
    method: metodo,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...cabecalhos },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  });
  const texto = await resposta.text();
  if (!resposta.ok) throw new Error(`${metodo} ${caminho} → ${resposta.status}: ${texto}`);
  return texto ? JSON.parse(texto) : null;
}

/** O token do admin: com a senha definitiva, ou trocando a inicial por ela. */
export async function tokenDoAdmin(api, senhaInicial, novaSenha) {
  const definitiva = await fetch(`${api}/api/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'admin', senha: novaSenha }),
  });
  if (definitiva.ok) return (await definitiva.json()).token;
  const inicial = await chamar(api, 'POST', '/api/auth/login', { login: 'admin', senha: senhaInicial });
  return (await chamar(api, 'POST', '/api/auth/trocar-senha', { senhaAtual: senhaInicial, novaSenha }, inicial.token)).token;
}

const skill = (name, description, content) => JSON.stringify({ name, description, content, arguments: [], repositories: [], isCommand: false, isBackground: false, isPinned: false }, null, 2);
const rule = (key, description, content) => JSON.stringify({ key, description, content, repository: '', onlyWithWorkItem: false, isRequired: false, isAgentInstruction: false }, null, 2);

/** O conteúdo da skill que o editor mostra (e o e2e edita). */
export const SKILL_NET = '# Compilar .NET Framework\n\n1. Ache o MSBuild pelo vswhere (Visual Studio 2019+).\n2. Compile só os .csproj que a task alterou, nunca a solução inteira.\n4. Erro de referência: rode o restore do NuGet do projeto antes.\n\n## Saída\n- O log vai para a entrega (devcli entrega --log).\n- Warning novo é pendência; o que já existia, não.';

/**
 * Monta o cenário. `marca` separa os nomes quando o mesmo banco recebe o cenário mais de uma vez (o e2e roda depois
 * dos outros cenários, na mesma base).
 */
export async function montarCenario687(api, token, codigoDeRegistro, marca = '') {
  const sufixo = marca ? ` ${marca}` : '';
  const login = (base) => (marca ? `${base}.${marca}` : base);

  // 1. As empresas: a Acme editada com os dados e o contrato; a Globex sem CNPJ e com o contrato vencido.
  const acme = await chamar(api, 'POST', '/api/empresas', { nome: `Acme Software${sufixo}`, plano: 'Empresarial', assentos: 25 }, token);
  const globex = await chamar(api, 'POST', '/api/empresas', { nome: `Globex Tecnologia${sufixo}`, plano: 'Empresarial', assentos: 10 }, token);
  await chamar(api, 'PUT', `/api/empresas/${globex.id}`, {
    nome: globex.nome, plano: 'Empresarial', assentos: 10, cnpj: '', segmento: 'Serviços', cidade: '', uf: '', contato: '', telefone: '', site: '', contratoAte: diaIso(-4),
  }, token);

  // 2. Os usuários e a licença de cada um, definida pelo admin (com o motivo no histórico).
  const criar = async (l, nome, papel, empresaId) => (await chamar(api, 'POST', '/api/usuarios', { login: l, nome, papel, empresaId }, token)).usuario;
  const ana = await criar(login('ana.lima'), 'Ana Lima', 'dev', acme.id);
  const bruno = await criar(login('bruno.souza'), 'Bruno Souza', 'gestor', acme.id);
  const devDaAcme = await criar(login('dev.acme'), 'Dev da Acme', 'dev', acme.id);
  await criar(login('gestor.globex'), '', 'gestor', globex.id);
  await chamar(api, 'PUT', `/api/usuarios/${ana.id}/licenca`, { modo: 'ate-data', ate: diaIso(3), tipo: 'trial', motivo: 'trial de 14 dias criado no cadastro' }, token);
  await chamar(api, 'PUT', `/api/usuarios/${devDaAcme.id}/licenca`, { modo: 'ate-data', ate: diaIso(6), tipo: 'individual', motivo: 'licença individual do piloto' }, token);

  // A Acme com os dados básicos e o contrato (a validade da licença de quem não tem a sua definida).
  await chamar(api, 'PUT', `/api/empresas/${acme.id}`, {
    nome: acme.nome, plano: 'Empresarial', assentos: 25, cnpj: '11.222.333/0001-81', segmento: 'Software / ERP', cidade: 'São Paulo', uf: 'SP',
    contato: 'gestor@acme.com.br', telefone: '', site: '', contratoAte: diaIso(142),
  }, token);

  // 3. A telemetria do painel de utilidade, por uma máquina registrada pelo código.
  const maquinaId = `maquina-687-${marca || 'local'}-${Date.now()}`;
  const registro = await chamar(api, 'POST', '/api/maquinas/registrar', { maquinaId, versaoDevKit: '5.2.0', codigoRegistro: codigoDeRegistro });
  const eventos = [];
  let n = 0;
  const evento = (tipo, detalhe, { dias = 0, horas = 0, valor = null, sessao = 's-687', quantidade = 1 } = {}) => eventos.push({
    eventId: `e687-${marca}-${maquinaId}-${n++}`, tipo, sessaoId: sessao, quantidade, valor, detalhe, em: new Date(Date.now() - dias * DIA - horas * 3_600_000).toISOString(),
  });
  // Saúde agora: 6 tasks paradas (4 nas últimas 24 h), as perguntas abertas, dois fechamentos que falharam e a máquina no limite.
  [17, 12, 3, 2, 1.5, 1].forEach((horas, i) => evento('SituacaoDaTask', 'parada-sem-processo', { horas: i < 2 ? horas * 24 : horas, sessao: `parada-${i}` }));
  for (let i = 0; i < 10; i++) evento('PerguntaAoDev', 'aberta', { horas: i === 0 ? 72 : i * 3, sessao: `pergunta-${i}` });
  evento('FechamentoDaEntrega', 'falhou', { horas: 5, valor: 669, sessao: 'fechamento-669' });
  evento('FechamentoDaEntrega', 'falhou', { horas: 9, valor: 585, sessao: 'fechamento-585' });
  evento('AmostraDaMaquina', 'disco-mb=8;memoria-mb=124;limite=disco+memoria', { horas: 0.2, valor: 8, sessao: '' });
  // O resultado: sessões aprovadas (uma devolvida antes), as notas e os turnos de cada dia pelo desfecho.
  for (let s = 0; s < 14; s++) {
    const dias = 1 + (s % 12);
    const sessao = `entrega-${s}`;
    evento('SessaoIniciada', '5.2.0', { dias, horas: 8, sessao });
    evento('ObjetivoAvaliado', '', { dias, horas: 6, valor: 84 + (s % 10), sessao });
    if (s % 2 === 0) evento('ObjetivoAvaliado', '', { dias, horas: 5, valor: 92, sessao });
    evento('ObjetivoCumprido', '', { dias, horas: 4, sessao });
    if (s % 7 === 0) evento('RevisaoHumana', 'devolvida', { dias, horas: 3, valor: 1_800_000, sessao });
    evento('RevisaoHumana', 'aprovada', { dias, horas: 1, valor: 9_000_000, sessao });
    if (s < 11) evento('FechamentoDaEntrega', 'ok', { dias, valor: 700 + s, sessao: `fechamento-${s}` });
  }
  for (let dia = 0; dia < 30; dia++) {
    if (dia % 7 === 5 || dia % 7 === 6) continue; // os fins de semana sem uso
    const base = 6 + ((dia * 7) % 9);
    // Cada turno conta o TurnoExecutado e o desfecho dele, como o dev.kit manda.
    const turno = (desfecho) => {
      evento('TurnoExecutado', 'claude', { dias: dia, valor: 60_000, sessao: `dia-${dia}` });
      evento('DesfechoDoTurno', desfecho, { dias: dia, sessao: `dia-${dia}` });
    };
    for (let t = 0; t < base; t++) turno('trabalhou');
    for (let t = 0; t < dia % 3; t++) turno('esperou-dev');
    if (dia % 4 === 1) turno('limite-de-uso');
    if (dia % 5 === 2) turno('falhou');
    evento('ComandoDelegado', 'dotnet.exe', { dias: dia, quantidade: 2, sessao: `dia-${dia}` });
    evento('ArquivoAlterado', '', { dias: dia, quantidade: 10, sessao: `dia-${dia}` });
  }
  // Onde as tasks travam: as sugestões de impacto da retrospectiva do Orquestrador.
  const sugestoes = [
    ['Esperou processo em segundo plano que morreu', 'pendente', ''], ['Pergunta sem chegar ao dev', 'promovida', '#568'],
    ['Limite de uso sem troca de agente', 'descartada', '#681'], ['Conflito não resolvido no fechamento', 'pendente', ''],
    ['CI vermelho por teste instável', 'promovida', ''], ['Disco cheio / memória', 'descartada', ''],
  ];
  let id = 1;
  sugestoes.forEach(([causa, estado, us], i) => {
    // As vezes anteriores foram descartadas; a mais nova (o maior id) tem o estado que vale para o selo.
    const vezes = 9 - i;
    for (let vez = 0; vez < vezes; vez++) {
      evento('SugestaoDeImpacto', `${causa}|sugestao-${id++}|${vez === vezes - 1 ? estado : 'descartada'}|${us}`, { dias: 1 + vez * 3, horas: i, sessao: '' });
    }
  });
  for (let inicio = 0; inicio < eventos.length; inicio += 400) {
    await chamar(api, 'POST', '/api/telemetria/lote', { versao: 'v1', maquinaId, versaoDevKit: '5.2.0', eventos: eventos.slice(inicio, inicio + 400) }, null,
      { 'X-Machine-Key': registro.chave });
  }

  // 4. O catálogo básico publicado e um rascunho (a v2 de uma skill).
  const itens = [
    ['skill', skill('compilar-net-framework', 'Compilar projetos .NET Framework (csproj antigo) com o MSBuild, só os projetos alterados.', SKILL_NET.replace('\n2. Compile só os .csproj que a task alterou, nunca a solução inteira.', '\n2. Compile só os .csproj que a task alterou, nunca a solução inteira.\n3. Rode o build.')), '.NET'],
    ['skill', skill('testar-dotnet', 'Testes unitários C# dos projetos afetados', '# Testar\n\n1. dotnet test do projeto alterado.'), '.NET'],
    ['skill', skill('compilar-angular', 'ng build após alterar .ts/.html/.scss', '# Compilar Angular\n\n1. ng build.'), 'Angular'],
    ['skill', skill('lancar-horas', 'Lançar as horas do dia nos work items', '# Lançar horas\n\n1. devcli horas.'), 'Azure DevOps'],
    ['skill', skill('commitar-e-enviar', 'Commit e push pelas regras do board', '# Commit\n\n1. git commit com Ab#.'), 'Git'],
    ['plan', rule('avaliar-testes', 'Os testes existem e cobrem a regressão', 'Toda mudança tem teste.'), 'Rules'],
    ['plan', rule('build-minimo', 'Build só do que foi alterado', 'Compile só o projeto alterado.'), 'Rules'],
    ['plan', rule('avaliar-arquitetura', 'Aspectos arquiteturais', 'Siga os Tech Plans.'), 'Rules'],
  ];
  const salvos = {};
  for (const [tipo, conteudo, categoria] of itens) {
    const item = await chamar(api, 'POST', '/api/catalogos/basico/itens', { tipo, conteudo, categoria }, token).catch(async (falha) => {
      // A base que já recebeu o cenário: o item existe, e é editado.
      if (!String(falha.message).includes('→ 409')) throw falha;
      return null;
    });
    if (item) salvos[item.nome] = item;
  }
  await chamar(api, 'POST', '/api/catalogos/basico/publicar', {}, token).catch(() => null);
  const net = salvos['compilar-net-framework'];
  if (net) {
    await chamar(api, 'PUT', `/api/catalogos/basico/itens/${net.id}`, {
      tipo: 'skill', categoria: '.NET',
      conteudo: skill('compilar-net-framework', 'Compilar projetos .NET Framework (csproj antigo) com o MSBuild, só os projetos alterados.',
        SKILL_NET.replace('\n4. Erro de', '\n3. Use -m:1 e -nodeReuse:false: a máquina do dev é compartilhada\n   por vários agentes e os nós do MSBuild não podem sobrar.\n4. Erro de')),
    }, token);
  }

  return { acme, globex, ana, bruno, devDaAcme, maquinaId, itemNet: net };
}

// Pela linha de comando: semeia a API local da recaptura.
if (import.meta.url === `file:///${process.argv[1]?.replace(/\\/g, '/')}`) {
  const [api, senha, codigo] = process.argv.slice(2);
  const token = await tokenDoAdmin(api, senha, `${senha}x`);
  const feito = await montarCenario687(api, token, codigo);
  console.log(JSON.stringify({ token, acme: feito.acme.id, ana: feito.ana.id, item: feito.itemNet?.id }));
}
