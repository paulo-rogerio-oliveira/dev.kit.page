import { useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { recursoDoCicloDosItens, type AcaoHumanaDoItem, type ImpactoDaDecisaoDoItem } from '../api/cicloDosItens';
import type { ItemDoProjeto, Projeto } from '../api/projetos';
import { formatar } from '../formatar';

const encerrado = (i: ItemDoProjeto) => !!i.decisaoHumana || ['concluido-manual', 'cancelado'].includes(i.situacao);
const podeDecidir = (p: Projeto) => ['dono', 'editor'].includes(p.papel ?? '') && p.situacao !== 'concluido';
const rotulo = (acao: AcaoHumanaDoItem) => acao === 'cancelar-item' ? 'Cancelar item' : 'Concluir manualmente';

interface Confirmacao {
  acao: AcaoHumanaDoItem; motivo: string; pedido: string;
  original?: Projeto; impacto?: ImpactoDaDecisaoDoItem; erro: string;
}

/** Consumidor do contrato do item4: efeitos vêm da simulação; só um clique humano aplica a decisão. */
export function DecisaoDoItem({ projeto, numero, token, atualizar }: {
  projeto: Projeto; numero: number; token: string; atualizar: (projeto: Projeto) => void;
}) {
  const [disponivel, setDisponivel] = useState(false);
  const [aviso, setAviso] = useState('Conferindo acesso e compatibilidade…');
  const [modal, setModal] = useState<Confirmacao | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const sequencia = useRef(0);
  const vivo = useRef(true);
  const atualizarRef = useRef(atualizar);
  atualizarRef.current = atualizar;
  const item = projeto.itens.find(i => i.numero === numero);

  useEffect(() => {
    vivo.current = true;
    const vez = ++sequencia.current;
    setDisponivel(false);
    Promise.all([api.recursosDosProjetos(token), api.projetoComCicloDosItens(token, projeto.id)]).then(([recursos, atual]) => {
      if (!vivo.current || sequencia.current !== vez) return;
      atualizarRef.current(atual);
      const compativel = recursos.recursos?.includes(recursoDoCicloDosItens) === true;
      setDisponivel(compativel && podeDecidir(atual));
      setAviso(!compativel ? 'Esta API ainda não oferece as decisões humanas de item. Atualize o serviço.'
        : !podeDecidir(atual) ? 'Somente dono ou editor pode decidir em um projeto aberto.' : '');
    }).catch(falha => {
      if (vivo.current && sequencia.current === vez) setAviso(mensagemDaFalha(falha));
    });
    return () => { vivo.current = false; sequencia.current++; };
  }, [token, projeto.id, numero]);

  async function preparar(acao: AcaoHumanaDoItem, motivo = '') {
    const vez = ++sequencia.current;
    const pedido = crypto.randomUUID();
    setOcupado(true);
    setModal({ acao, motivo, pedido, erro: '' });
    try {
      const atual = await api.projetoComCicloDosItens(token, projeto.id);
      if (!vivo.current || sequencia.current !== vez) return;
      atualizarRef.current(atual);
      const alvo = atual.itens.find(i => i.numero === numero);
      if (!podeDecidir(atual)) throw new ErroDaApi(403, 'Seu acesso não permite decidir. Releia o projeto.');
      if (!alvo || encerrado(alvo)) throw new ErroDaApi(409, 'O item já foi encerrado ou não está mais disponível. Consulte o recibo e o histórico.');
      const impacto = await api.decidirItemDoProjeto(token, projeto.id, numero, { pedido, acao, versaoEsperada: atual.versao });
      if (!vivo.current || sequencia.current !== vez) return;
      if (impacto.projeto.versao !== atual.versao) throw new ErroDaApi(409, 'O projeto mudou durante a simulação. Releia antes de confirmar.');
      setModal({ acao, motivo, pedido, original: atual, impacto, erro: '' });
    } catch (falha) {
      if (vivo.current && sequencia.current === vez) setModal({ acao, motivo, pedido, erro: mensagemDaFalha(falha) });
    } finally { if (vivo.current && sequencia.current === vez) setOcupado(false); }
  }

  async function confirmar() {
    if (!modal?.impacto || !modal.original || ocupado || !modal.motivo.trim() || modal.motivo.length > 2000) return;
    const decisao = modal;
    const original = modal.original;
    const vez = ++sequencia.current;
    setOcupado(true);
    try {
      const resposta = await api.decidirItemDoProjeto(token, projeto.id, numero, {
        pedido: decisao.pedido, acao: decisao.acao, versaoEsperada: original.versao,
        aplicar: true, confirmar: true, motivo: decisao.motivo.trim(),
        confirmacao: `${original.slug}/${numero}/${decisao.acao}/remota:${original.versao}`,
      });
      if (!vivo.current || sequencia.current !== vez) return;
      atualizarRef.current(resposta.projeto);
      setDisponivel(podeDecidir(resposta.projeto));
      setModal(null);
      setAviso('Decisão registrada no recibo. A integração e a parada física continuam sendo conferidas separadamente.');
    } catch (falha) {
      if (!vivo.current || sequencia.current !== vez) return;
      const erro = `${mensagemDaFalha(falha)} Releia e revise os efeitos antes de confirmar novamente.`;
      setModal({ ...decisao, impacto: undefined, original: undefined, erro });
      // Releitura não reaplica nem simula sozinha; o motivo continua no diálogo.
      try {
        const atual = await api.projetoComCicloDosItens(token, projeto.id);
        if (vivo.current && sequencia.current === vez) {
          atualizarRef.current(atual);
          setDisponivel(podeDecidir(atual));
        }
      } catch { /* A falha original e o rascunho continuam visíveis; confirmar permanece bloqueado. */ }
    } finally { if (vivo.current && sequencia.current === vez) setOcupado(false); }
  }

  function fechar() {
    if (ocupado) return;
    sequencia.current++;
    setModal(null);
  }

  if (!item) return <p role="status">Item não disponível no retrato atual.</p>;
  const recibo = item.decisaoHumana;
  return <section className="projeto-item-detalhe" aria-label={`Detalhe do item ${numero}`}>
    <div className="projeto-item-cabecalho">
      <div><h3>Item {numero} · {item.titulo}</h3><p>As ações são decisões humanas e não substituem a integração.</p></div>
      <div className="projeto-item-acoes">
        <button className="botao item-concluir" disabled={!disponivel || ocupado || encerrado(item)}
          onClick={() => void preparar('concluir-manualmente')}>Concluir manualmente</button>
        <button className="botao botao-perigo" disabled={!disponivel || ocupado || encerrado(item)}
          onClick={() => void preparar('cancelar-item')}>Cancelar item</button>
      </div>
    </div>
    {item.descricao && <p>{item.descricao}</p>}
    <dl className="projeto-item-informacoes">
      <div><dt>Situação</dt><dd>{recibo ? rotulo(recibo.acao) : item.situacao} · onda {item.onda}</dd></div>
      <div><dt>Entrega parcial</dt><dd>{item.branch ? `Branch: ${item.branch}. ` : ''}Consulte Entregas e Histórico para verificar o trabalho preservado.</dd></div>
      <div><dt>Dependentes</dt><dd>{projeto.itens.filter(i => i.dependencias.includes(numero)).map(i => i.numero).join(', ') || 'Nenhum item depende deste no plano registrado'}</dd></div>
      <div><dt>Seu papel</dt><dd>{projeto.papel === 'dono' ? 'Dono' : projeto.papel === 'editor' ? 'Editor' : projeto.papel === 'leitor' ? 'Leitor' : 'Acesso ainda não confirmado'}</dd></div>
    </dl>
    {recibo && <div className="projeto-item-recibo" role="status">
      <strong>{recibo.acao === 'cancelar-item' ? 'Cancelado pelo dev' : 'Concluído pelo dev'}</strong>
      <p>{recibo.motivo}</p><p>Autor: {recibo.autor} · {formatar.dataHora(recibo.em)}</p>
      <p>Pedido: <code>{recibo.pedido}</code></p>
      {recibo.paradaPendente && <p>Pedido de parada registrado. A parada física depende da confirmação do host; as reservas são preservadas até essa confirmação.</p>}
    </div>}
    {aviso && <p role="status">{aviso}</p>}
    <p className="projetos-nota">Concluir manualmente e Cancelar item são distintos de Integrado e Excluir item. Entrega, histórico e commits já integrados são preservados.</p>
    {modal && <ConfirmacaoDoItem modal={modal} projeto={projeto} item={item} ocupado={ocupado} fechar={fechar}
      motivo={texto => setModal(atual => atual ? { ...atual, motivo: texto } : atual)}
      revisar={() => void preparar(modal.acao, modal.motivo)} confirmar={() => void confirmar()} />}
  </section>;
}

function ConfirmacaoDoItem({ modal, projeto, item, ocupado, fechar, motivo, revisar, confirmar }: {
  modal: Confirmacao; projeto: Projeto; item: ItemDoProjeto; ocupado: boolean; fechar: () => void;
  motivo: (texto: string) => void; revisar: () => void; confirmar: () => void;
}) {
  const dialogo = useRef<HTMLDialogElement>(null);
  const campo = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    const anterior = document.activeElement as HTMLElement | null;
    const overflow = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden';
    dialogo.current?.showModal();
    campo.current?.focus();
    return () => { document.documentElement.style.overflow = overflow; anterior?.focus(); };
  }, []);
  const cancelar = modal.acao === 'cancelar-item';
  return <dialog ref={dialogo} className="item-decisao-modal" aria-labelledby="item-decisao-titulo"
    onCancel={e => { e.preventDefault(); fechar(); }}>
    <div className="item-decisao-punho" aria-hidden="true" />
    <h2 id="item-decisao-titulo">{cancelar ? 'Cancelar' : 'Concluir manualmente'} o item {item.numero}?</h2>
    <p className="item-decisao-alvo">{projeto.nome}<br /><code>{item.branch}</code></p>
    <div className="item-decisao-aviso"><strong>Decisão humana, não integração</strong>
      <p>O item sai do curso das ondas. Ele não passa a contar como integrado e não é excluído do plano.</p></div>
    <dl className="item-decisao-efeitos">
      <div><dt>Dependentes</dt><dd>{modal.impacto ? modal.impacto.dependentes.join(', ') || 'Nenhum item espera por este' : 'Aguardando revisão dos efeitos'}</dd></div>
      <div><dt>Entrega parcial</dt><dd>Branch e commits preservados</dd></div>
      <div><dt>Histórico</dt><dd>Continua acessível, com autor e data</dd></div>
      <div><dt>Já integrado</dt><dd>Nada é revertido</dd></div>
    </dl>
    {modal.impacto && <p className="item-decisao-consequencia">{modal.impacto.consequencia}</p>}
    <label htmlFor="item-decisao-motivo">Motivo da decisão</label>
    <textarea ref={campo} id="item-decisao-motivo" maxLength={2000} value={modal.motivo} readOnly={ocupado}
      onChange={e => motivo(e.target.value)} aria-describedby="item-decisao-ajuda" />
    <p id="item-decisao-ajuda" className="item-decisao-ajuda">O motivo é obrigatório, tem até 2.000 caracteres e fica registrado com o seu usuário.</p>
    {ocupado && <p role="status">Conferindo a decisão…</p>}
    {modal.erro && <p role="alert" className="erro">{modal.erro}</p>}
    <div className="item-decisao-botoes">
      <button className="botao botao-fantasma" disabled={ocupado} onClick={fechar}>Voltar</button>
      {!modal.impacto && !ocupado ? <button className="botao" onClick={revisar}>Reler e revisar</button>
        : <button className={`botao ${cancelar ? 'botao-perigo' : 'item-concluir'}`}
          disabled={ocupado || !modal.impacto || !modal.motivo.trim() || modal.motivo.length > 2000} onClick={confirmar}>{rotulo(modal.acao)}</button>}
    </div>
  </dialog>;
}
