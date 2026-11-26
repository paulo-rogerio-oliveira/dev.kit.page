// Demonstração isolada: dist real do Board e DTOs de teste em memória. Nenhuma credencial ou escrita externa.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = resolve(dirname(fileURLToPath(import.meta.url)), '../dist');
const porta = Number(process.env.DEVKIT_DEMO_PACOTE_PORTA ?? process.argv[2] ?? 4186);
const cenarios = new Map();
const tipos = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.webp': 'image/webp' };
const agora = () => new Date().toISOString();
const repositorios = ['https://github.com/acme/git.kit', 'https://github.com/acme/dev.kit.page'];
const pedidoAtual = 'a'.repeat(32), pedidoAnterior = 'b'.repeat(32);
function cenario(estado) {
  if (cenarios.has(estado)) return cenarios.get(estado);
  const projeto = { id: 11, slug: 'fixes', nome: 'fixes', nivel: 'usuario', time: '', repositorios, branch: 'feature/dev/projeto-fixes',
    semanaConvertida: false, definicao: 'Pacote do projeto integrado.', anexos: [], autonomia: true, paralelismo: 3, destinoDaPr: 'develop',
    situacao: 'aprovado', versao: 7, autor: 'Demonstração', atualizadoEm: agora(), itens: [], podeApagar: false, featureId: 0, epicId: 0,
    iteracao: '', board: 'nao', papel: estado === 'leitor' ? 'leitor' : 'editor', skillDoPacote: 'pacote-demo',
    skillsDoPacote: [{ nome: 'pacote-demo', tipo: 'somente-comando' }, { nome: 'pacote-com-parametros', tipo: 'com-parametros' }] };
  const resultado = (i, antigo = false) => {
    const web = i === 1;
    const ciclo = antigo ? 'publicado' : estado === 'gerando' ? 'agente-pendente' : estado === 'falhou' ? 'falhou'
      : estado === 'local' ? 'local-pronto' : web ? 'nao-aplicavel' : 'publicado';
    return { id: (antigo ? 1 : 7) + i, projetoId: 11, tipo: web ? 'preview' : 'pacote', repositorio: repositorios[i],
      sha: (web ? '7' : '8').repeat(40), situacao: ciclo === 'falhou' ? 'falhou' : ciclo === 'agente-pendente' ? 'gerando' : 'pronto',
      link: ciclo === 'publicado' ? `http://127.0.0.1:${porta}/resultado-demo/${web ? 'previa' : 'pacote.zip'}` : '',
      validoAte: ciclo === 'publicado' ? new Date(Date.now() + (estado === 'vencido' && !antigo ? -60000 : 23 * 3600_000)).toISOString() : null,
      hash: web ? '' : 'a'.repeat(64), tamanho: web ? 0 : 43_200_000, ambiente: web ? 'fixes' : '',
      detalhe: antigo ? 'Resultado anterior da demonstração.' : ciclo === 'agente-pendente' ? 'Agente resolvendo os parâmetros da skill.'
        : ciclo === 'falhou' ? 'SDK necessário ausente no host. Instale a capacidade indicada pelo serviço antes de gerar novamente.'
          : ciclo === 'local-pronto' ? 'Resultado local preservado. O registro remoto ainda não foi concluído.'
            : web ? 'Destino da prévia não configurado. Informe o Static Web App da prévia do projeto. Nada foi compilado nem publicado neste repositório.'
              : 'Gerado pela skill pacote-demo: aplicativo, agendador, CLI e sandbox.',
      criadoEm: new Date(Date.now() - (antigo ? 86400_000 : 12 * 60000)).toISOString(),
      idRemoto: ciclo === 'publicado' ? (antigo ? 41 : 51) + i : 0, estado: ciclo,
      ciclo: { estado: ciclo, etapa: 'demonstracao', branch: projeto.branch, pedidoId: antigo ? pedidoAnterior : pedidoAtual },
    };
  };
  const pedidos = [false, true].map(antigo => ({ id: antigo ? pedidoAnterior : pedidoAtual, projetoId: 11,
    criadoEm: resultado(0, antigo).criadoEm, estado: antigo ? 'publicado' : estado === 'gerando' ? 'em-andamento' : estado === 'falhou' ? 'falhou'
      : estado === 'local' ? 'local-pronto' : estado === 'vencido' ? 'expirado' : 'publicado', resultados: [resultado(0, antigo), resultado(1, antigo)] }));
  const comando = { id: 'c-demo', maquinaId: 'demo', tipo: 'projeto', texto: JSON.stringify({ acao: 'artefato', projeto: 'fixes' }),
    dispositivo: 'desktop', estado: 'concluido', criadoEm: pedidos[0].criadoEm, expiraEm: new Date(Date.now() + 3600_000).toISOString(),
    entregueEm: agora(), concluidoEm: agora(), erro: '' };
  const origens = { tipo: 'origens-artefato', projeto: 'fixes', resultados: repositorios.map((r, i) => ({ Repositorio: r,
    Sha: (i ? '7' : '8').repeat(40), Formato: i ? 'preview' : 'pacote', Aplicavel: true,
    Produtor: i ? 'prévia web' : 'pacote-demo', Motivo: i ? 'Front Vite' : 'Skill pacote-demo · roda direto' })) };
  const comandos = [comando, { ...comando, id: 'c-anterior', criadoEm: pedidos[1].criadoEm },
    { ...comando, id: 'c-previsao', texto: JSON.stringify({ acao: 'artefato', projeto: 'fixes', conferir: true }) }];
  const respostas = [pedidoAtual, pedidoAnterior].map((id, i) => ({ id: `r-${i}`, comandoId: comandos[i].id, maquinaId: 'demo', tipo: 'concluido',
    texto: `Pedido ${id}: publicado. Resultado por repositório.`, geradaEm: agora(), recebidaEm: agora() }));
  respostas.push({ ...respostas[0], id: 'r-previsao', comandoId: 'c-previsao', texto: JSON.stringify(origens) });
  const dados = { projeto, pedidos, origens, comandos, respostas, eventos: [], leiturasDosLinks: 0 };
  cenarios.set(estado, dados);
  return dados;
}
function json(res, dados, status = 200) { res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(dados)); }
export const servidor = createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://127.0.0.1:${porta}`);
    const contexto = new URL(req.headers.referer ?? url, url);
    const estado = url.searchParams.get('estado') ?? contexto.searchParams.get('demoPacote') ?? 'gerado';
    const dados = cenario(estado);
    const maquina = { maquinaId: 'demo', nome: 'Máquina de demonstração', versaoDevKit: '156', origem: 'servico', appAberto: true,
      estadoDoAgente: 'ativo', turnoEmAndamento: false, conectada: true, ultimoHeartbeatEm: agora(), retratoGeradoEm: agora(), vinculada: true };
    if (url.pathname === '/__pacote') {
      const sessao = { token: 'token-exclusivo-demonstracao', expiraEm: new Date(Date.now() + 86400_000).toISOString(), deveTrocarSenha: false,
        login: 'demo@example.test', ehAdmin: false, papel: 'dev', empresa: 'Demonstração' };
      res.writeHead(200, { 'Content-Type': 'text/html' });
      return res.end(`<script>sessionStorage.setItem('devkitpage.sessao',${JSON.stringify(JSON.stringify(sessao))});location.replace('/board?aba=board&demoPacote=${encodeURIComponent(estado)}');</script>`);
    }
    if (url.pathname.startsWith('/api/')) {
      if (url.pathname === '/api/remoto/maquinas') return json(res, [maquina]);
      if (url.pathname.endsWith('/retrato')) return json(res, { maquina, recebidoEm: agora(), retrato: { contrato: 1, maquinaId: 'demo', geradoEm: agora(), pendencias: [], fluxos: [],
        projetos: [{ ...dados.projeto, ondaAtual: 3, itens: [], podeGerarArtefato: estado !== 'leitor', artefatos: dados.pedidos[0].resultados.map(r => ({ id: r.id, tipo: r.tipo,
          repositorio: r.repositorio, sha: r.sha, estado: r.estado, etapa: r.ciclo.etapa, detalhe: r.detalhe, idRemoto: r.idRemoto, criadoEm: r.criadoEm })) }] } });
      if (url.pathname.endsWith('/decisoes')) return json(res, []);
      if (url.pathname.endsWith('/catalogo')) return json(res, { maquinaId: 'demo', recebidoEm: agora(), catalogo: { contrato: 1, maquinaId: 'demo', geradoEm: agora(),
        comandoId: null, workItems: [], repositorios: [], fluxos: [], projetos: [], designs: [], agentes: [], membros: [] } });
      if (url.pathname.endsWith('/conversa')) return json(res, { comandos: dados.comandos, respostas: dados.respostas, falas: [], ultimoEvento: 0 });
      if (url.pathname.endsWith('/eventos')) {
        res.writeHead(200, { 'Content-Type': 'text/event-stream' });
        const cursor = Number(req.headers['last-event-id'] ?? 0);
        return res.end(dados.eventos.filter(e => e.id > cursor).map(e => `data: ${JSON.stringify(e)}\n\n`).join('') || 'event: ping\ndata: \n\n');
      }
      if (url.pathname === '/api/projetos/resumos') return json(res, [{ ...dados.projeto, totalDeItens: 7, itensConcluidos: 3 }]);
      if (url.pathname === '/api/projetos/11') return json(res, dados.projeto);
      if (url.pathname.includes('/artefatos/pedidos/')) return json(res, dados.pedidos.find(p => p.id === url.pathname.split('/').at(-1)), 200);
      if (url.pathname.endsWith('/artefatos')) {
        if (estado === 'vencido' && dados.leiturasDosLinks++ > 0)
          for (const r of dados.pedidos[0].resultados.filter(r => r.estado === 'publicado')) r.validoAte = new Date(Date.now() + 23 * 3600_000).toISOString();
        return json(res, dados.pedidos.flatMap(p => p.resultados).filter(r => r.estado === 'publicado').map(r => ({
          ...r, id: r.idRemoto, expiraEm: r.validoAte, autor: 'Demonstração' })));
      }
      if (url.pathname.endsWith('/skill-do-pacote') && req.method === 'PUT') {
        let texto = ''; for await (const parte of req) texto += parte;
        const edicao = JSON.parse(texto);
        if (edicao.versaoEsperada !== dados.projeto.versao) return json(res, { detail: 'O projeto mudou. Releia antes de salvar.' }, 409);
        dados.projeto.skillDoPacote = edicao.skillDoPacote; dados.projeto.versao++;
        return json(res, dados.projeto);
      }
      if (url.pathname === '/api/remoto/comandos' && req.method === 'POST') {
        let texto = ''; for await (const parte of req) texto += parte;
        const corpo = JSON.parse(texto), opcoes = JSON.parse(corpo.texto || '{}');
        if (opcoes.retomar) {
          const r = dados.pedidos[0].resultados.find(r => r.id === opcoes.retomar);
          if (r) { r.estado = 'publicado'; r.ciclo.estado = 'publicado'; r.idRemoto = 51; r.link = `http://127.0.0.1:${porta}/resultado-demo/pacote.zip`;
            r.validoAte = new Date(Date.now() + 23 * 3600_000).toISOString(); r.detalhe = 'Retomada concluída no mesmo ID pelo cenário isolado.'; }
        }
        const comando = { ...dados.comandos[0], ...corpo, estado: 'concluido', criadoEm: agora() };
        const resposta = { ...dados.respostas[0], id: `r-${corpo.id}`, comandoId: corpo.id,
          texto: opcoes.conferir ? JSON.stringify(dados.origens) : `Pedido ${pedidoAtual}: em-andamento. Demonstração isolada.` };
        dados.comandos.push(comando); dados.respostas.push(resposta);
        for (const par of [{ tipo: 'comando', comando, resposta: null }, { tipo: 'resposta', comando: null, resposta }])
          dados.eventos.push({ id: dados.eventos.length + 1, maquinaId: 'demo', em: agora(), ...par });
        return json(res, { ...comando, estado: 'na-fila' }, 202);
      }
      if (url.pathname.endsWith('/push')) return json(res, { disponivel: false, chavePublica: null });
      return json(res, []);
    }
    if (url.pathname.startsWith('/resultado-demo/')) { res.writeHead(200, { 'Content-Type': 'text/plain' }); return res.end('Resultado fictício da demonstração, sem pacote publicado.'); }
    let arquivo = resolve(raiz, '.' + decodeURIComponent(url.pathname));
    if (arquivo !== raiz && !arquivo.startsWith(raiz + '/') && !arquivo.startsWith(raiz + '\\')) { res.writeHead(403); return res.end(); }
    if (!extname(arquivo) || !(await stat(arquivo).catch(() => null))?.isFile()) arquivo = resolve(raiz, 'index.html');
    let bytes = await readFile(arquivo);
    if (arquivo.endsWith('index.html')) bytes = Buffer.from(bytes.toString().replace('</body>', `<script>
      const abrirPacote=new MutationObserver(()=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent==='Ver pacote do projeto');
      if(b){abrirPacote.disconnect();b.click();}});abrirPacote.observe(document.body,{childList:true,subtree:true});
    </script></body>`));
    res.writeHead(200, { 'Content-Type': tipos[extname(arquivo)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }); res.end(bytes);
  } catch (e) { if (!res.headersSent) res.writeHead(500); res.end(String(e)); }
}).listen(porta, '127.0.0.1', () => process.stdout.write(`Board real com dados isolados: http://127.0.0.1:${porta}/__pacote?estado=gerado\n`));
