import { useState } from 'react';
import type { ItemDoProjeto, Projeto } from '../api/projetos';
import { EditorDoItem } from './projetos/EditorDoItem';

const estados: Record<string, string> = { planejado: 'Planejado', rodando: 'Rodando', integrado: 'Integrado', falhou: 'Falhou', pergunta: 'Pergunta' };
const curto = (url: string) => url.replace(/\.git$/, '').split(/[/:]/).filter(Boolean).slice(-2).join('/');
export function fluxoDoItem(projeto: Projeto, item: ItemDoProjeto) {
  const valor = item.fluxo || projeto.fluxoPadrao || '';
  const nome = valor.startsWith('ref:') ? valor.slice(4).split('@')[0].split(':').slice(1).join(':').replace(/-/g, ' ') : valor;
  return `${nome || 'Fluxo pendente'} · ${item.fluxo ? 'item' : projeto.fluxoPadrao ? 'projeto' : 'escolha pendente'}`;
}

export function OndasDoProjeto({ projeto, itens, ondaAtual, salvo, tratar }: {
  projeto: Projeto; itens: ItemDoProjeto[]; ondaAtual: number; salvo: (p: Projeto) => void; tratar: (e: unknown) => string;
}) {
  const [filtro, setFiltro] = useState('');
  const [busca, setBusca] = useState('');
  const [editor, setEditor] = useState<ItemDoProjeto | 'novo' | null>(null);
  const [mensagem, setMensagem] = useState('');
  const editar = ['dono', 'editor'].includes(projeto.papel ?? '') && !['fechando', 'concluido'].includes(projeto.situacao);
  const visiveis = itens.filter(i => (!filtro || i.situacao === filtro) && `${i.numero} ${i.titulo}`.toLocaleLowerCase().includes(busca.toLocaleLowerCase()));
  const ondas = [...new Set(itens.map(i => i.onda))].sort((a, b) => a - b);
  return <div>
    <div className="projeto-filtros-itens" role="group" aria-label="Filtrar itens">
      {['', 'planejado', 'rodando', 'integrado'].map(f => <button key={f} className="botao botao-fantasma" aria-pressed={filtro === f} onClick={() => setFiltro(f)}>
        {f ? estados[f] : 'Todos'} · {itens.filter(i => !f || i.situacao === f).length}</button>)}
      <input aria-label="Buscar item" placeholder="Buscar item…" value={busca} onChange={e => setBusca(e.target.value)} />
      {editar && <button className="botao botao-fantasma" onClick={() => setEditor('novo')}>+ Item</button>}
    </div>
    {mensagem && <p role="status">{mensagem}</p>}
    {visiveis.length === 0 && <p role="status">{itens.length ? 'Nenhum item corresponde aos filtros.' : 'Ainda sem itens no plano.'}</p>}
    <div className="projeto-ondas">{ondas.filter(o => visiveis.some(i => i.onda === o)).map(onda => <section className="projeto-onda" key={onda} aria-label={`Onda ${onda}`} aria-current={onda === ondaAtual ? 'step' : undefined}>
      <h3>Onda {onda}<small>{itens.filter(i => i.onda === onda && i.situacao === 'integrado').length} de {itens.filter(i => i.onda === onda).length}</small></h3>
      {visiveis.filter(i => i.onda === onda).map(i => <div className="projeto-item" key={i.numero}>
        <div className="projeto-item-topo"><span className={`projeto-situacao ${i.situacao}`}>{estados[i.situacao] ?? i.situacao}</span>
          {editar && <button className="botao botao-fantasma" disabled={i.situacao !== 'planejado'} onClick={() => setEditor(i)} aria-label={`Editar item ${i.numero}`}>Editar ↗</button>}</div>
        <h4>{i.numero} · {i.titulo}</h4>
        <p>{i.dependencias.length ? `Depende de: ${i.dependencias.join(', ')}` : 'Sem dependências'}</p>
        <p title={i.fluxo || projeto.fluxoPadrao || ''}>{fluxoDoItem(projeto, i)}</p>
        <span className="projeto-situacao">{i.agente && i.modelo ? `${i.agente} · ${i.modelo}` : 'Sem modelo selecionado'}</span>
        <p>{(i.repositorios.length ? i.repositorios : projeto.repositorios).map(curto).join(' · ')}</p>
        {i.branch && <code>{i.branch}</code>}
        {i.workItemId > 0 && <small>Work item #{i.workItemId}</small>}
      </div>)}
    </section>)}</div>
    {editor && <EditorDoItem projeto={projeto} item={editor === 'novo' ? null : editor} fechar={() => setEditor(null)} tratar={tratar}
      salvo={p => { salvo(p); setEditor(null); setMensagem('Item salvo. O plano aguarda aprovação; nenhuma execução foi disparada.'); }} />}
  </div>;
}
