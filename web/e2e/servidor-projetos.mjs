// Somente demonstração/teste local: serve o dist real e encaminha /api à API com SQLite descartável.
import { createServer, request as proxyRequest } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { resolve, extname, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const aqui = dirname(fileURLToPath(import.meta.url));
const raiz = resolve(aqui, '../dist');
const api = `http://localhost:${process.env.DEVKIT_E2E_API_PORT ?? 5199}`;
const porta = Number(process.env.DEVKIT_E2E_WEB_PORT ?? 4199);
const tipos = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.webp': 'image/webp' };
const falhasDoDetalhe = new Set();
async function demonstracao(res, url) {
  const estado = url.searchParams.get('estado') ?? 'plano';
  const principal = estado === 'sem-itens' ? 'fixes-sem-itens' : 'fixes';
  let login = await fetch(`${api}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'admin', senha: 'senhaNovaE2e2026' }) });
  if (!login.ok) {
    const inicial = await fetch(`${api}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'admin', senha: 'senhaInicialE2e2026' }) });
    const sessao = await inicial.json();
    login = await fetch(`${api}/api/auth/trocar-senha`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${sessao.token}` }, body: JSON.stringify({ senhaAtual: 'senhaInicialE2e2026', novaSenha: 'senhaNovaE2e2026' }) });
  }
  if (!login.ok) throw new Error(`Login da base temporária recusado: ${login.status}`);
  const sessao = await login.json();
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${sessao.token}` };
  const existentes = await (await fetch(`${api}/api/projetos`, { headers })).json();
  const modelo = JSON.parse(await readFile(resolve(aqui, 'projetos-demo.json'), 'utf8'));
  for (const [slug, nome] of [[principal, estado === 'sem-itens' ? 'fixes · plano vazio' : 'fixes'], ['runners', 'runners'], ['ajustes-2', 'ajustes_2']]) {
    const p = { ...modelo, slug, nome, versao: existentes.find(p => p.slug === slug)?.versao ?? null };
    if (estado === 'sem-itens' && slug === principal) p.itens = [];
    if (estado.startsWith('detalhe-') && slug === 'runners') p.itens = p.itens.map(i => ({ ...i, titulo: 'runners: ' + i.titulo }));
    if (url.searchParams.get('estado') === 'modelos') { p.agente = ''; p.modelo = ''; p.modelos = []; p.itens = p.itens.map(i => ({ ...i, agente: '', modelo: '' })); }
    const escrita = await fetch(`${api}/api/projetos`, { method: 'PUT', headers, body: JSON.stringify(p) });
    if (!escrita.ok) throw new Error(`Semente temporária recusada: ${escrita.status} ${await escrita.text()}`);
  }
  const projetos = await (await fetch(`${api}/api/projetos/resumos`, { headers })).json();
  const alvo = projetos.find(p => p.slug === 'runners').id;
  falhasDoDetalhe.delete(alvo);
  const destino = `/projetos?slug=${principal}&visao=${estado === 'plano' ? 'plano' : 'ondas'}&nivel=usuario&extra=contexto&demonstracao=${encodeURIComponent(estado)}&alvo=${alvo}`;
  res.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' });
  res.end(`<script>sessionStorage.setItem('devkitpage.sessao',${JSON.stringify(JSON.stringify(sessao))});location.replace(${JSON.stringify(destino)});</script>`);
}
createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://localhost:${porta}`);
    if (url.pathname === '/__demo' && process.env.DEVKIT_DEMO_PROJETOS === '1') return await demonstracao(res, url);
    if (url.pathname.startsWith('/api/')) {
      const contexto = new URL(req.headers.referer ?? `http://localhost:${porta}`);
      const estado = contexto.searchParams.get('demonstracao');
      const alvo = Number(contexto.searchParams.get('alvo'));
      if (['detalhe-erro', 'detalhe-recuperado'].includes(estado) && url.pathname === `/api/projetos/${alvo}` && !falhasDoDetalhe.has(alvo)) {
        falhasDoDetalhe.add(alvo);
        res.writeHead(503, { 'Content-Type': 'application/json' });
        return res.end('{"detail":"Detalhe temporariamente indisponível (proxy isolado)."}');
      }
      if (url.pathname === '/api/projetos/resumos' && ['erro', 'vazio'].includes(estado)) {
        res.writeHead(estado === 'erro' ? 503 : 200, { 'Content-Type': 'application/json' }); return res.end(estado === 'erro' ? '{"detail":"Rede temporariamente indisponível (demonstração isolada)."}' : '[]');
      }
      if (url.pathname === '/api/projetos/resumos' && estado === 'carregando') await new Promise(r => setTimeout(r, 15_000));
      const proxy = proxyRequest(new URL(req.url, api), { method: req.method, headers: req.headers }, resposta => {
        res.writeHead(resposta.statusCode, resposta.headers); resposta.pipe(res);
      });
      proxy.on('error', e => { if (!res.headersSent) res.writeHead(502); res.end(e.message); }); req.pipe(proxy); return;
    }
    let arquivo = resolve(raiz, '.' + decodeURIComponent(url.pathname));
    if (!arquivo.startsWith(raiz + '/') && !arquivo.startsWith(raiz + '\\') && arquivo !== raiz) { res.writeHead(403); return res.end(); }
    if (!extname(arquivo) || !(await stat(arquivo).catch(() => null))?.isFile()) arquivo = resolve(raiz, 'index.html');
    let bytes = await readFile(arquivo);
    const estado = url.searchParams.get('demonstracao');
    if (arquivo.endsWith('index.html') && process.env.DEVKIT_DEMO_PROJETOS === '1' && (estado?.startsWith('detalhe-') || estado === 'sem-resultados')) {
      const preparar = `<script>
        const estadoDaCaptura=${JSON.stringify(estado)}, alvoDaCaptura=${JSON.stringify(url.searchParams.get('alvo'))};
        if(estadoDaCaptura==='detalhe-carregando'){
          const fetchReal=window.fetch.bind(window);let reter=true,liberar;
          window.fetch=(...args)=>fetchReal(...args).then(async resposta=>{
            if(reter && new URL(String(args[0]),location.href).pathname==='/api/projetos/'+alvoDaCaptura){
              await resposta.clone().arrayBuffer();await new Promise(r=>liberar=r);
            }return resposta;
          });
          document.addEventListener('click',e=>{if(e.target.closest('button')?.textContent.includes('Atualizar')){reter=false;liberar?.();}},true);
        }
        let mudou=false;
        const observerDaCaptura=new MutationObserver(()=>{
          if(!mudou && document.querySelector('[aria-label="Editar item 4"]')){
            mudou=true;
            const campo=document.querySelector(estadoDaCaptura==='sem-resultados'?'[aria-label="Buscar item"]':'[aria-label="Projeto ativo"]');
            const tipo=estadoDaCaptura==='sem-resultados'?HTMLInputElement:HTMLSelectElement;
            Object.getOwnPropertyDescriptor(tipo.prototype,'value').set.call(campo,estadoDaCaptura==='sem-resultados'?'Busca sem correspondência':alvoDaCaptura);
            campo.dispatchEvent(new Event(estadoDaCaptura==='sem-resultados'?'input':'change',{bubbles:true}));
          }
          if(estadoDaCaptura==='detalhe-recuperado'){
            const reler=[...document.querySelectorAll('button')].find(b=>b.textContent==='Reler projeto');
            if(reler){observerDaCaptura.disconnect();reler.click();}
          }
        });observerDaCaptura.observe(document.body,{childList:true,subtree:true});
      </script>`;
      bytes = Buffer.from(bytes.toString().replace('</body>', preparar + '</body>'));
    }
    if (arquivo.endsWith('index.html') && process.env.DEVKIT_DEMO_PROJETOS === '1' && ['modal', 'conflito', 'salvo'].includes(estado)) {
      const preparar = `<script>
        const estado=${JSON.stringify(estado)};
        const observer=new MutationObserver(async()=>{
          const b=document.querySelector('[aria-label="Editar item 4"]'); if(!b)return;
          observer.disconnect(); b.click(); if(estado==='modal')return;
          await new Promise(r=>setTimeout(r,50));
          const headers={'Content-Type':'application/json',Authorization:'Bearer '+JSON.parse(sessionStorage.getItem('devkitpage.sessao')).token};
          const resumos=await(await fetch('/api/projetos/resumos',{headers})).json();
          const id=resumos.find(p=>p.slug==='fixes').id;
          const atual=await(await fetch('/api/projetos/'+id,{headers})).json();
          if(estado==='conflito'){
            const i=atual.itens.find(i=>i.numero===5);
            await fetch('/api/projetos/'+id+'/itens/5',{method:'PUT',headers,body:JSON.stringify({versaoEsperada:atual.versao,titulo:'Edição de outro cliente',descricao:i.descricao,fluxo:i.fluxo,agente:i.agente,modelo:i.modelo,dependencias:i.dependencias,componentes:i.componentes,prioridade:i.prioridade})});
          }
          const campo=document.querySelector('dialog input');
          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(campo,estado==='salvo'?'Edição persistida na API':'Rascunho preservado');
          campo.dispatchEvent(new Event('input',{bubbles:true}));
          await new Promise(r=>setTimeout(r,0)); document.querySelector('dialog form').requestSubmit();
        });observer.observe(document.body,{childList:true,subtree:true});
      </script>`;
      bytes = Buffer.from(bytes.toString().replace('</body>', preparar + '</body>'));
    }
    res.writeHead(200, { 'Content-Type': tipos[extname(arquivo)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }); res.end(bytes);
  } catch (e) { res.writeHead(500, { 'Content-Type': 'text/plain' }); res.end(e.message); }
}).listen(porta, '127.0.0.1', () => process.stdout.write(`Demonstração local: http://127.0.0.1:${porta}\n`));
