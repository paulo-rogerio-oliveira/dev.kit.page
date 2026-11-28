// Demonstração isolada (boards-estados, item 5): o dist real do site e DTOs de teste em memória, para recapturar a página
// do projeto e o Board. Nenhuma credencial, nenhuma escrita externa: o pedido à máquina para aqui, "na fila".
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = resolve(dirname(fileURLToPath(import.meta.url)), '../dist');
const porta = Number(process.env.DEVKIT_DEMO_BOARDS_PORTA ?? process.argv[2] ?? 4187);
const tipos = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.webp': 'image/webp' };
const estados = ['aguardando-disparo', 'aprovar', 'enviado', 'em-execucao', 'manual', 'leitor', 'board'];
const agora = () => new Date().toISOString();
const disparo = '2026-11-27T19:40:00Z';

const item = (numero, titulo, onda, dependencias, workItemId, tipo = 'User Story') => ({ numero, titulo, descricao: '', fluxo: '14',
  repositorios: [], componentes: ['board'], dependencias, onda, workItemId, situacao: 'planejado', tipo, prioridade: 1, horas: 0,
  branch: '', agente: 'codex', modelo: 'gpt-6.1-sol' });
const plano = [
  item(1, 'Espelho configurável', 1, [], 813), item(5, 'Board web: Aprovar e Disparar', 1, [], 817),
  item(2, 'Separar Aprovar de Disparar', 2, [1], 814),
  item(3, 'Aplicar os estados', 3, [1, 2], 815), item(4, 'Desktop: nível e estados', 3, [1, 2], 816), item(6, 'Corrigir o nível do Bug', 3, [1], 818, 'Bug'),
];

/** O projeto do estado pedido: o mesmo plano, em cada ponto da vida dele. */
function projetoDo(estado) {
  const base = { id: 21, slug: 'boards-estados', nome: 'Projetos em qualquer nível do Boards', nivel: 'usuario', time: '',
    repositorios: ['https://github.com/paulo-rogerio-oliveira/git.kit', 'https://github.com/paulo-rogerio-oliveira/dev.kit.page'],
    branch: 'feature/dev/projeto-boards-estados', baseDoBranch: 'develop', semanaConvertida: false, anexos: [], autonomia: true, paralelismo: 2,
    definicao: 'Projetos em qualquer nível do Azure Boards, com Aprovar e Disparar separados e o ciclo de estados.', destinoDaPr: 'develop',
    situacao: 'aprovado', versao: 6, autor: 'Demonstração', atualizadoEm: agora(), podeApagar: true, papel: 'dono', featureId: 812, epicId: 0,
    iteracao: '', board: 'criar', fluxoPadrao: '14', agente: 'codex', modelo: 'gpt-6.1-sol', modelos: [{ agente: 'codex', modelo: 'gpt-6.1-sol' }],
    nivelNoBoard: 'feature', estadosNoBoard: { pai: { inicial: '', execucao: '', finalizacao: '' }, itens: { inicial: '', execucao: '', finalizacao: 'Resolved' } },
    disparadoEm: null, itens: plano, skillDoPacote: '', skillsDoPacote: [] };
  if (['aprovar', 'enviado'].includes(estado)) return { ...base, situacao: 'aguardando', featureId: 0, itens: plano.map(i => ({ ...i, workItemId: 0 })) };
  if (estado === 'em-execucao') return { ...base, disparadoEm: disparo, itens: plano.map(i => i.onda === 1 ? { ...i, situacao: 'rodando' } : i) };
  if (estado === 'manual') return { ...base, autonomia: false, disparadoEm: disparo, itens: plano.map(i => i.onda === 1 ? { ...i, situacao: 'integrado' } : i) };
  if (estado === 'leitor') return { ...base, papel: 'leitor' };
  return base;
}

function resumos(estado) {
  const p = projetoDo(estado);
  const resumo = (r, extra = {}) => ({ ...r, totalDeItens: r.itens.length, itensConcluidos: r.itens.filter(i => i.situacao === 'integrado').length,
    itensEmExecucao: r.itens.filter(i => i.situacao === 'rodando').length, itensComPergunta: 0, itensFalhos: 0, progresso: 0, temPendencias: false, ...extra });
  const outro = (id, slug, nome, situacao, extra = {}) => resumo({ ...p, id, slug, nome, situacao, itens: plano }, extra);
  return [resumo(p), outro(22, 'fixes', 'fixes', 'aprovado', { disparadoEm: '2026-11-25T12:00:00Z', itensConcluidos: 5 }),
    outro(23, 'runners', 'runners', 'aguardando'), outro(24, 'ajustes-2', 'ajustes_2', 'concluido', { itensConcluidos: 6 }),
    outro(25, 'artefato', 'artefato', 'rascunho')].map(({ itens, ...r }) => r);
}

const maquina = { maquinaId: 'desktop-paulo', nome: 'DESKTOP-PAULO', versaoDevKit: '211', origem: 'servico', appAberto: true,
  estadoDoAgente: 'ativo', turnoEmAndamento: false, conectada: true, ultimoHeartbeatEm: agora(), retratoGeradoEm: agora(), vinculada: true };
const itemNoRetrato = (numero, situacao, onda) => ({ numero, titulo: `Item ${numero}`, situacao, onda });
const retrato = () => ({ maquina: { ...maquina, ultimoHeartbeatEm: agora() }, recebidoEm: agora(), retrato: { contrato: 1, maquinaId: maquina.maquinaId,
  geradoEm: agora(), pendencias: [], fluxos: [{ id: 'exec-fixes-7', nome: 'Item 7 · fixes', etapa: 'Implementação', situacao: 'rodando', projeto: 'fixes', item: 7, agente: 'codex' }],
  projetos: [
    { slug: 'boards-estados', nome: 'Projetos em qualquer nível do Boards', situacao: 'aprovado', ondaAtual: 1, podeGerarArtefato: true,
      itens: plano.map(i => itemNoRetrato(i.numero, 'planejado', i.onda)) },
    { slug: 'fixes', nome: 'fixes', situacao: 'aprovado', ondaAtual: 3, podeGerarArtefato: true,
      itens: [itemNoRetrato(1, 'integrado', 1), itemNoRetrato(2, 'integrado', 2), itemNoRetrato(7, 'rodando', 3), itemNoRetrato(8, 'planejado', 4)] },
    { slug: 'runners', nome: 'runners', situacao: 'aguardando', ondaAtual: 0, podeGerarArtefato: true, itens: [itemNoRetrato(1, 'planejado', 1)] },
  ] } });

function json(res, dados, status = 200) { res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(dados)); }
async function corpo(req) { let texto = ''; for await (const parte of req) texto += parte; return texto ? JSON.parse(texto) : null; }

export const servidor = createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://127.0.0.1:${porta}`);
    const contexto = new URL(req.headers.referer ?? url, url);
    const pedido = url.searchParams.get('estado') ?? url.searchParams.get('demoBoards') ?? contexto.searchParams.get('demoBoards') ?? 'aguardando-disparo';
    const estado = estados.includes(pedido) ? pedido : 'aguardando-disparo';
    // O capturador headless pode impor largura mínima à janela: a moldura põe a página real num iframe do tamanho exato,
    // sem escala, para a imagem de 390 ser o layout de 390 de verdade.
    if (url.pathname === '/__moldura') {
      const largura = Number(url.searchParams.get('largura') ?? 390), altura = Number(url.searchParams.get('altura') ?? 980);
      res.writeHead(200, { 'Content-Type': 'text/html' });
      return res.end(`<!doctype html><html><body style="margin:0;background:#fff"><iframe title="Site real" src="/__boards?estado=${encodeURIComponent(estado)}"
        style="display:block;border:0;width:${largura}px;height:${altura}px"></iframe></body></html>`);
    }
    if (url.pathname === '/__boards') {
      const sessao = { token: 'token-exclusivo-demonstracao', expiraEm: new Date(Date.now() + 86400_000).toISOString(), deveTrocarSenha: false,
        login: 'admin', ehAdmin: true, papel: 'admin', empresa: null };
      const destino = estado === 'board' ? `/board?aba=board&demoBoards=board`
        : `/projetos?slug=boards-estados&visao=ondas&nivel=usuario&demoBoards=${encodeURIComponent(estado)}`;
      res.writeHead(200, { 'Content-Type': 'text/html' });
      return res.end(`<script>sessionStorage.setItem('devkitpage.sessao',${JSON.stringify(JSON.stringify(sessao))});location.replace(${JSON.stringify(destino)});</script>`);
    }
    if (url.pathname.startsWith('/api/')) {
      const projeto = projetoDo(estado);
      if (url.pathname === '/api/projetos/resumos') return json(res, resumos(estado));
      if (/^\/api\/projetos\/\d+$/.test(url.pathname)) {
        const id = Number(url.pathname.split('/').at(-1));
        const resumo = resumos(estado).find(r => r.id === id);
        return id === projeto.id ? json(res, projeto) : resumo ? json(res, { ...projeto, ...resumo, itens: plano }) : json(res, { detail: 'Projeto não encontrado.' }, 404);
      }
      if (url.pathname.endsWith('/prontidao-disparo')) return json(res, { acao: url.searchParams.get('acao') ?? 'disparar', pendencia: null });
      if (url.pathname.endsWith('/acessos')) return json(res, { recurso: 'projeto', projeto: projeto.id, nome: projeto.nome, papel: projeto.papel,
        dono: { id: 1, login: 'admin', nome: '' }, acessos: [], candidatos: [] });
      if (url.pathname === '/api/remoto/maquinas') return json(res, [{ ...maquina, ultimoHeartbeatEm: agora() }]);
      if (url.pathname === '/api/remoto/comandos' && req.method === 'POST') {
        const novo = await corpo(req);
        return json(res, { ...novo, estado: 'na-fila', criadoEm: agora(), expiraEm: new Date(Date.now() + 900_000).toISOString(),
          entregueEm: null, concluidoEm: null, erro: '' }, 202);
      }
      if (url.pathname.startsWith('/api/remoto/comandos/')) return json(res, { id: url.pathname.split('/').at(-1), maquinaId: maquina.maquinaId,
        tipo: 'projeto', texto: '', dispositivo: 'navegador', estado: 'na-fila', criadoEm: agora(), expiraEm: agora(), entregueEm: null, concluidoEm: null, erro: '' });
      if (url.pathname.endsWith('/retrato')) return json(res, retrato());
      if (url.pathname.endsWith('/decisoes')) return json(res, []);
      if (url.pathname.endsWith('/catalogo')) return json(res, { maquinaId: maquina.maquinaId, recebidoEm: agora(), catalogo: { contrato: 1, maquinaId: maquina.maquinaId,
        geradoEm: agora(), comandoId: null, workItems: [], repositorios: [], fluxos: [], projetos: [], designs: [], agentes: [], membros: [] } });
      if (url.pathname.endsWith('/conversa')) return json(res, { comandos: [], respostas: [], falas: [], ultimoEvento: 0 });
      if (url.pathname.endsWith('/eventos')) { res.writeHead(200, { 'Content-Type': 'text/event-stream' }); return res.end('event: ping\ndata: \n\n'); }
      if (url.pathname.endsWith('/push')) return json(res, { disponivel: false, chavePublica: null });
      return json(res, []);
    }
    let arquivo = resolve(raiz, '.' + decodeURIComponent(url.pathname));
    if (arquivo !== raiz && !arquivo.startsWith(raiz + '/') && !arquivo.startsWith(raiz + '\\')) { res.writeHead(403); return res.end(); }
    if (!extname(arquivo) || !(await stat(arquivo).catch(() => null))?.isFile()) arquivo = resolve(raiz, 'index.html');
    let bytes = await readFile(arquivo);
    // Aprovar e enviado abrem o pedido pela ação real da tela; o enviado escolhe a máquina e envia, como o dev faria.
    if (arquivo.endsWith('index.html') && ['aprovar', 'enviado'].includes(estado)) bytes = Buffer.from(bytes.toString().replace('</body>', `<script>
      const estadoDaDemo=${JSON.stringify(estado)};let fase=0;
      const acao=new MutationObserver(()=>{
        const aprovar=[...document.querySelectorAll('button')].find(b=>b.textContent.trim().endsWith('Aprovar plano'));
        if(fase===0&&aprovar&&!aprovar.disabled){fase=1;aprovar.click();if(estadoDaDemo==='aprovar')acao.disconnect();return;}
        const enviar=[...document.querySelectorAll('button')].find(b=>b.textContent==='Enviar pedido');
        if(fase===1&&enviar&&!enviar.disabled){fase=2;acao.disconnect();enviar.click();}
      });acao.observe(document.body,{childList:true,subtree:true,attributes:true});
    </script></body>`));
    res.writeHead(200, { 'Content-Type': tipos[extname(arquivo)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }); res.end(bytes);
  } catch (e) { if (!res.headersSent) res.writeHead(500); res.end(String(e)); }
}).listen(porta, '127.0.0.1', () => process.stdout.write(`Site real com dados isolados: http://127.0.0.1:${porta}/__boards?estado=aguardando-disparo\n`));
