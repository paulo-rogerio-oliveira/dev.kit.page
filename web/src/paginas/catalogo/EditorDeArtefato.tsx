import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';
import { api, ErroDaApi } from '../../api/cliente';
import type { AnexoDoItem, CatalogoNomeado, DiferencasDoItem, HistoricoDoItem, ItemEditavel, RelatorioDePublicacao, TipoDeItem } from '../../api/tipos';
import { Selo } from '../../componentes/Selo';
import { salvarArquivo } from '../../arquivos';
import { formatar } from '../../formatar';
import {
  lerFluxo, lerPlano, lerSkill, lerSkillMarkdown, montarPlano, montarSkill, PLANO_VAZIO, SKILL_VAZIA, skillMarkdown, tamanhoEmBytes, tamanhoLegivel,
  TAMANHO_MAXIMO_DO_CONTEUDO, textoDoFluxo, type CamposDaSkill, type CamposDoPlano,
} from './conteudo';
import { NOME_DO_TIPO, rotuloDaCategoria } from './tipos';

/** Quanto o editor espera depois da última tecla para salvar o rascunho sozinho. */
export const ESPERA_DO_RASCUNHO_MS = 1500;

/** As stacks que a Rule escolhe (a lista do git.kit); vazia é qualquer stack. */
export const STACKS = ['', 'dotnet', 'angular', 'vb6'];

const ROTULO_DO_ESCOPO: Record<string, string> = { base: 'Base (todos)', empresa: 'Empresa', projeto: 'Projeto (repositório)' };

type Aba = 'conteudo' | 'diferencas' | 'historico' | 'anexos';

/** O estado editável de um item, pelo tipo: o texto do editor e os campos que não estão nele. */
interface Rascunho {
  texto: string;
  nome: string;
  descricao: string;
  categoria: string;
  stack: string;
  skill: CamposDaSkill;
  plano: CamposDoPlano;
}

function rascunhoDe(tipo: TipoDeItem, item: ItemEditavel | null): Rascunho {
  if (tipo === 'skill') {
    const skill = item?.conteudo ? lerSkill(item.conteudo) : { ...SKILL_VAZIA, name: 'nova-skill', description: 'O que a skill faz', content: '# Passo a passo\n\n1. ' };
    return { texto: skillMarkdown(skill), nome: skill.name, descricao: skill.description, categoria: item?.categoria ?? '', stack: '', skill, plano: PLANO_VAZIO };
  }
  if (tipo === 'plan') {
    const plano = item?.conteudo ? lerPlano(item.conteudo) : { ...PLANO_VAZIO, key: 'nova-rule', description: 'Nova Rule', content: 'As regras do time.' };
    return { texto: plano.content, nome: plano.key, descricao: plano.description, categoria: item?.categoria ?? '', stack: plano.stack ?? '', skill: SKILL_VAZIA, plano };
  }
  const texto = item?.conteudo ? textoDoFluxo(item.conteudo) : '';
  const lido = lerFluxo(texto);
  return { texto, nome: item?.nome ?? lido.nome, descricao: lido.descricao, categoria: item?.categoria ?? '', stack: '', skill: SKILL_VAZIA, plano: PLANO_VAZIO };
}

/** O JSON que vai para a API (o mesmo que o dev.kit grava) e os erros que impedem salvar. */
function montar(tipo: TipoDeItem, r: Rascunho): { conteudo: string; erros: string[]; checagem: string } {
  if (tipo === 'skill') {
    const md = lerSkillMarkdown(r.texto);
    const skill = { ...r.skill, name: r.nome, description: r.descricao, content: md.content };
    const erros = [...md.erros, ...(md.name && md.name !== r.nome.trim() ? ['O "name" do frontmatter é diferente do Nome.'] : [])];
    if (!skill.content.trim()) erros.push('A skill está sem conteúdo depois do frontmatter.');
    return { conteudo: montarSkill(skill), erros, checagem: erros.length === 0 ? 'frontmatter ok' : 'frontmatter com erro' };
  }
  if (tipo === 'plan') {
    const erros = r.descricao.trim() ? [] : ['A Rule está sem descrição.'];
    if (!r.texto.trim()) erros.push('A Rule está sem conteúdo.');
    return { conteudo: montarPlano({ ...r.plano, key: r.nome, description: r.descricao, content: r.texto, stack: r.stack }), erros, checagem: erros.length === 0 ? 'conteúdo ok' : 'conteúdo com erro' };
  }
  const lido = lerFluxo(r.texto);
  return { conteudo: r.texto.trim(), erros: lido.erros, checagem: lido.erros.length === 0 ? 'fluxo ok' : 'fluxo com erro' };
}

/**
 * O EDITOR DE ARTEFATO (US #687, proposta admin-catalogo v010): o nome, a categoria, a stack, o escopo e a descrição; o
 * conteúdo (o SKILL.md, a Rule, o JSON do fluxo) com as linhas alteradas desde a versão publicada; as diferenças, o
 * histórico e os anexos; a validação (frontmatter, nome único, tamanho); o rascunho que se salva sozinho, o "Descartar
 * rascunho" e o "Publicar vN" — que gera a release nova do catálogo, a que o dev.kit sincroniza. O rascunho nunca chega
 * às máquinas.
 */
export function EditorDeArtefato({ token, item, tipo, catalogos, catalogoDoNovo, todos, aoMudar, aoDescartar, aoVerVitrine, tratar }: {
  token: string;
  item: ItemEditavel | null;
  tipo: TipoDeItem;
  catalogos: CatalogoNomeado[];
  /** O catálogo do item novo (o slug); o existente é o dele. */
  catalogoDoNovo: string;
  /** Os itens da lista (para o "nome único" e as categorias). */
  todos: ItemEditavel[];
  aoMudar: (item: ItemEditavel) => void;
  aoDescartar: () => void;
  aoVerVitrine: () => void;
  tratar: (falha: unknown) => string;
}) {
  const [atual, setAtual] = useState<ItemEditavel | null>(item);
  const [slug, setSlug] = useState(item?.catalogo ?? catalogoDoNovo);
  const [rascunho, setRascunho] = useState<Rascunho>(() => rascunhoDe(tipo, item));
  const [carregando, setCarregando] = useState(item !== null && item.conteudo === null);
  const [sujo, setSujo] = useState(false);
  const [estado, setEstado] = useState<'salvo' | 'salvando' | 'erro'>('salvo');
  const [mensagem, setMensagem] = useState('');
  const [aba, setAba] = useState<Aba>('conteudo');
  const [diferencas, setDiferencas] = useState<DiferencasDoItem | null>(null);
  const [historico, setHistorico] = useState<HistoricoDoItem[] | null>(null);
  const [anexos, setAnexos] = useState<AnexoDoItem[] | null>(null);
  const [relatorio, setRelatorio] = useState<RelatorioDePublicacao | null>(null);
  const salvando = useRef(false);

  const falhar = useCallback((falha: unknown) => setMensagem(tratar(falha)), [tratar]);

  // O conteúdo só vem no detalhe do item (a lista o traz nulo).
  useEffect(() => {
    if (!item || item.conteudo !== null) return;
    let vivo = true;
    api.itemDoCatalogo(token, item.catalogo ?? catalogoDoNovo, item.id)
      .then((detalhe) => {
        if (!vivo) return;
        setAtual(detalhe);
        setRascunho(rascunhoDe(tipo, detalhe));
      })
      .catch((falha) => vivo && falhar(falha))
      .finally(() => vivo && setCarregando(false));
    return () => {
      vivo = false;
    };
  }, [item, token, tipo, catalogoDoNovo, falhar]);

  const recarregarApoio = useCallback(async (id: number, slugDoItem: string) => {
    try {
      const [d, h, a] = await Promise.all([
        api.diferencasDoItem(token, slugDoItem, id), api.historicoDoItem(token, slugDoItem, id), api.anexosDoItem(token, slugDoItem, id),
      ]);
      setDiferencas(d);
      setHistorico(h);
      setAnexos(a);
    } catch (falha) {
      falhar(falha);
    }
  }, [token, falhar]);

  // As diferenças, o histórico e os anexos acompanham cada versão salva do item.
  const idDoItem = atual?.id;
  const hashDoItem = atual?.hash;
  useEffect(() => {
    if (idDoItem !== undefined) void recarregarApoio(idDoItem, slug);
  }, [idDoItem, hashDoItem, slug, recarregarApoio]);

  const montado = useMemo(() => montar(tipo, rascunho), [tipo, rascunho]);
  const bytes = tamanhoEmBytes(montado.conteudo);
  const nomeRepetido = todos.some((i) => i.tipo === tipo && i.nome === rascunho.nome.trim() && i.id !== atual?.id && (i.catalogo ?? slug) === slug);
  const tamanhoOk = bytes <= TAMANHO_MAXIMO_DO_CONTEUDO;
  const valido = montado.erros.length === 0 && !nomeRepetido && tamanhoOk;

  // O RASCUNHO SALVO SOZINHO: depois de uma pausa, o que é válido vai para a API (o mesmo PUT/POST do "Salvar rascunho").
  useEffect(() => {
    if (!sujo || !valido) return;
    const espera = setTimeout(async () => {
      if (salvando.current) return;
      salvando.current = true;
      setEstado('salvando');
      try {
        const corpo = { tipo, conteudo: montado.conteudo, ...(rascunho.categoria.trim() ? { categoria: rascunho.categoria.trim() } : {}) };
        const salvo = atual ? await api.editarItem(token, slug, atual.id, corpo) : await api.criarItem(token, slug, corpo);
        setAtual(salvo);
        setSujo(false);
        setEstado('salvo');
        setMensagem('');
        aoMudar(salvo);
      } catch (falha) {
        setEstado('erro');
        falhar(falha);
      } finally {
        salvando.current = false;
      }
    }, ESPERA_DO_RASCUNHO_MS);
    return () => clearTimeout(espera);
  }, [sujo, valido, montado.conteudo, rascunho.categoria, tipo, atual, token, slug, aoMudar, falhar]);

  function mudar(parcial: Partial<Rascunho>) {
    let proximo = { ...rascunho, ...parcial };
    if (tipo === 'skill') {
      // O Nome e a Descrição e o frontmatter andam juntos: quem muda um muda o outro.
      if (parcial.texto !== undefined) {
        const md = lerSkillMarkdown(parcial.texto);
        proximo = { ...proximo, nome: md.name || proximo.nome, descricao: md.description || proximo.descricao };
      } else if (parcial.nome !== undefined || parcial.descricao !== undefined) {
        const md = lerSkillMarkdown(proximo.texto);
        proximo = { ...proximo, texto: skillMarkdown({ name: proximo.nome, description: proximo.descricao, content: md.content }) };
      }
    }
    if (tipo === 'fluxo' && parcial.texto !== undefined) {
      const lido = lerFluxo(parcial.texto);
      proximo = { ...proximo, nome: lido.nome ? lido.nome : proximo.nome, descricao: lido.descricao };
    }
    setRascunho(proximo);
    setSujo(true);
  }

  async function descartar() {
    if (!atual) {
      aoDescartar();
      return;
    }
    const pergunta = atual.versaoPublicada > 0
      ? `Descartar o rascunho de ${atual.nome}? Ele volta à v${atual.versaoPublicada}, a que as máquinas têm.`
      : `Descartar ${atual.nome}? Ele nunca foi publicado e some do catálogo.`;
    if (!window.confirm(pergunta)) return;
    try {
      const voltou = await api.descartarRascunho(token, slug, atual.id);
      if (!voltou) {
        aoDescartar();
        return;
      }
      setAtual(voltou);
      setRascunho(rascunhoDe(tipo, voltou));
      setSujo(false);
      aoMudar(voltou);
      setMensagem(`Rascunho descartado: ${voltou.nome} voltou à v${voltou.versaoPublicada}.`);
    } catch (falha) {
      falhar(falha);
    }
  }

  async function publicar() {
    setRelatorio(null);
    try {
      const conferencia = await api.conferirPublicacao(token, slug);
      if (!conferencia.podePublicar) {
        setRelatorio(conferencia);
        return;
      }
      const outros = conferencia.mudancas.filter((m) => !m.includes(`:${atual?.nome} `) && !m.endsWith(`:${atual?.nome}`));
      if (!window.confirm(`Publicar a versão ${conferencia.proxima} do catálogo?${outros.length > 0 ? `\nVão junto: ${outros.join('; ')}` : ''}`)) return;
      const feita = await api.publicarCatalogo(token, slug);
      setMensagem(`Publicada a ${feita.release.tag}: as máquinas recebem na próxima sincronia.`);
      const recarregado = atual ? await api.itemDoCatalogo(token, slug, atual.id) : null;
      if (recarregado) {
        setAtual(recarregado);
        aoMudar(recarregado);
        // Publicar não muda o hash do conteúdo: as diferenças (agora com a versão nova) e o histórico recarregam aqui.
        await recarregarApoio(recarregado.id, slug);
      }
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 409 && falha.corpo && typeof falha.corpo === 'object' && 'problemas' in falha.corpo) {
        setRelatorio(falha.corpo as RelatorioDePublicacao);
        return;
      }
      falhar(falha);
    }
  }

  async function anexar(evento: ChangeEvent<HTMLInputElement>) {
    const arquivo = evento.target.files?.[0];
    evento.target.value = '';
    if (!arquivo || !atual) return;
    try {
      await api.anexarAoItem(token, slug, atual.id, arquivo);
      setAnexos(await api.anexosDoItem(token, slug, atual.id));
    } catch (falha) {
      falhar(falha);
    }
  }

  async function removerAnexo(anexo: AnexoDoItem) {
    if (!atual || !window.confirm(`Remover o anexo ${anexo.nome}?`)) return;
    try {
      await api.removerAnexo(token, slug, atual.id, anexo.id);
      setAnexos(await api.anexosDoItem(token, slug, atual.id));
    } catch (falha) {
      falhar(falha);
    }
  }

  const rascunhoPendente = !atual || atual.situacao === 'novo' || atual.situacao === 'alterado' || sujo;
  const proxima = (atual?.versaoPublicada ?? 0) + 1;
  const alteradas = new Set((diferencas?.linhas ?? []).filter((l) => l.tipo === 'adicionada' && l.numero !== null).map((l) => l.numero as number));
  const linhasDoTexto = rascunho.texto.split('\n');
  const categorias = [...new Set([rascunho.categoria, ...todos.map((i) => i.categoria)].filter(Boolean))].sort((a, b) => a.localeCompare(b, 'pt-BR'));
  const escopo = atual?.escopo ?? (catalogos.find((c) => c.slug === slug)?.basico ? 'base' : 'empresa');
  const rotuloDoConteudo = tipo === 'skill' ? 'Conteúdo (SKILL.md)' : tipo === 'plan' ? 'Conteúdo (Rule)' : 'Conteúdo (fluxo JSON)';

  if (carregando) return <section className="cartao editor-de-artefato"><p className="carregando" role="status">Carregando…</p></section>;

  return (
    <section className="cartao editor-de-artefato" aria-label={`Editor de ${rascunho.nome || NOME_DO_TIPO[tipo]}`}>
      <header className="editor-topo">
        <Selo variante={tipo === 'skill' ? 'sucesso' : tipo === 'plan' ? 'papel' : 'aviso'}>{NOME_DO_TIPO[tipo]}</Selo>
        <h2 className="fonte-mono">{rascunho.nome || 'novo item'}</h2>
        {rascunhoPendente && (
          <span className="pilula pilula-rascunho">{atual && atual.versaoPublicada > 0 ? `rascunho da v${proxima} · não publicado` : 'rascunho · nunca publicado'}</span>
        )}
        <div className="acoes editor-acoes">
          <button className="botao botao-fantasma botao-pequeno" type="button" onClick={aoVerVitrine}>Ver na vitrine</button>
          <button className="botao botao-fantasma botao-pequeno" type="button" disabled={!rascunhoPendente} onClick={() => void descartar()}>Descartar rascunho</button>
          <button className="botao botao-primario botao-pequeno" type="button" disabled={!atual || sujo || !rascunhoPendente} onClick={() => void publicar()}>Publicar v{proxima}</button>
        </div>
      </header>

      {mensagem && <p className={estado === 'erro' ? 'erro' : 'aviso'} role={estado === 'erro' ? 'alert' : 'status'}>{mensagem}</p>}
      {relatorio && relatorio.problemas.length > 0 && (
        <ul className="erro lista-de-erros" role="alert" aria-label="Problemas da publicação">
          {relatorio.problemas.map((p) => <li key={p}>{p}</li>)}
        </ul>
      )}

      <div className="editor-campos">
        <label>
          Nome
          <input className="fonte-mono" value={rascunho.nome} disabled={!!atual || tipo === 'fluxo'} onChange={(e) => mudar({ nome: e.target.value })} maxLength={80} />
        </label>
        <label>
          Categoria
          <select value={rascunho.categoria} onChange={(e) => mudar({ categoria: e.target.value })}>
            <option value="">{atual ? rotuloDaCategoria(atual.categoria) : 'derivada do nome'}</option>
            {categorias.map((c) => <option key={c} value={c}>{rotuloDaCategoria(c)}</option>)}
          </select>
        </label>
        <label>
          Stack
          <select value={rascunho.stack} disabled={tipo !== 'plan'} title={tipo === 'plan' ? undefined : 'Só a Rule tem stack no dev.kit.'}
            onChange={(e) => mudar({ stack: e.target.value })}>
            {(STACKS.includes(rascunho.stack) ? STACKS : [rascunho.stack, ...STACKS]).map((s) => <option key={s} value={s}>{s || (tipo === 'plan' ? 'qualquer' : '—')}</option>)}
          </select>
        </label>
        <label>
          Escopo
          <select value={atual ? escopo : slug} disabled={!!atual} onChange={(e) => setSlug(e.target.value)}
            title="Para onde o item vai: o básico (toda máquina), um catálogo de empresa, ou preso a um repositório (projeto).">
            {atual
              ? <option value={escopo}>{ROTULO_DO_ESCOPO[escopo] ?? escopo}</option>
              : catalogos.map((c) => <option key={c.slug} value={c.slug}>{c.basico ? ROTULO_DO_ESCOPO.base : `Empresa · ${c.nome}`}</option>)}
          </select>
        </label>
      </div>
      <label className="editor-descricao">
        Descrição (aparece na vitrine e no combo do agente)
        <input value={rascunho.descricao} disabled={tipo === 'fluxo'} onChange={(e) => mudar({ descricao: e.target.value })} maxLength={500} />
      </label>

      <div className="abas-do-editor" role="tablist" aria-label="Partes do item">
        {([
          ['conteudo', rotuloDoConteudo],
          ['diferencas', diferencas && diferencas.versaoPublicada > 0 ? `Diferenças com a v${diferencas.versaoPublicada}` : 'Diferenças'],
          ['historico', 'Histórico'],
          ['anexos', `Arquivos anexos${anexos ? ` ${anexos.length}` : ''}`],
        ] as [Aba, string][]).map(([id, rotulo]) => (
          <button key={id} role="tab" type="button" aria-selected={aba === id} className="aba-do-editor" onClick={() => setAba(id)}>{rotulo}</button>
        ))}
      </div>

      {aba === 'conteudo' && (
        <div className="editor-codigo">
          <pre className="editor-gutter" aria-hidden="true">
            {linhasDoTexto.map((_, i) => <span key={i} className={alteradas.has(i + 1) ? 'linha-alterada' : undefined}>{i + 1}{'\n'}</span>)}
          </pre>
          <div className="editor-camadas">
            <pre className="editor-fundo" aria-hidden="true">
              {linhasDoTexto.map((l, i) => <span key={i} className={alteradas.has(i + 1) ? 'linha-alterada' : undefined}>{l || ' '}{'\n'}</span>)}
            </pre>
            <textarea aria-label={rotuloDoConteudo} spellCheck={false} value={rascunho.texto} rows={Math.max(16, linhasDoTexto.length + 1)}
              onChange={(e) => mudar({ texto: e.target.value })} />
          </div>
        </div>
      )}

      {aba === 'diferencas' && (
        <div className="diferencas" aria-label="Diferenças com a versão publicada">
          {!diferencas ? <p className="texto-mudo">Salve o rascunho para comparar.</p>
            : diferencas.linhas.length === 0 ? <p className="texto-mudo">Sem conteúdo.</p>
              : (
                <pre className="trace">
                  {diferencas.linhas.map((l, i) => (
                    <span key={i} className={`diferenca-${l.tipo}`}>{l.tipo === 'adicionada' ? '+ ' : l.tipo === 'removida' ? '− ' : '  '}{l.texto}{'\n'}</span>
                  ))}
                </pre>
              )}
          {diferencas && <p className="texto-mudo">{diferencas.adicionadas} linha(s) nova(s) · {diferencas.removidas} removida(s) desde a v{diferencas.versaoPublicada || '—'}.</p>}
        </div>
      )}

      {aba === 'historico' && (
        <ul className="historico-do-item" aria-label="Histórico do item">
          {(historico ?? []).length === 0 && <li className="texto-mudo">Sem histórico ainda.</li>}
          {(historico ?? []).map((h, i) => (
            <li key={`${h.em}-${i}`}>
              {formatar.dataHora(h.em)} · {h.acao === 'publicado' ? `publicada a v${h.versao} (${h.release})` : h.acao === 'rascunho' ? 'rascunho salvo'
                : h.acao === 'descartado' ? `rascunho descartado (volta à v${h.versao})` : 'retirado'} · {h.autor}
            </li>
          ))}
        </ul>
      )}

      {aba === 'anexos' && (
        <div className="anexos-do-item">
          <p className="nota-da-secao">Material de apoio de quem edita: fica no site e não vai para as máquinas.</p>
          <ul>
            {(anexos ?? []).map((a) => (
              <li key={a.id}>
                <button className="link" type="button" onClick={async () => { try { salvarArquivo(await api.baixarAnexo(token, slug, atual!.id, a)); } catch (falha) { falhar(falha); } }}>
                  {a.nome}
                </button>
                <small className="texto-mudo"> {tamanhoLegivel(a.tamanhoBytes)} · {a.autor}</small>
                <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Remover ${a.nome}`} onClick={() => void removerAnexo(a)}>Remover</button>
              </li>
            ))}
          </ul>
          <label className="botao botao-fantasma botao-pequeno anexar">
            Anexar arquivo
            <input type="file" className="visualmente-oculto" disabled={!atual} onChange={(e) => void anexar(e)} />
          </label>
        </div>
      )}

      <footer className="editor-rodape">
        <span className={valido ? 'validacao-ok' : 'validacao-erro'} role="status">
          ● Validação: {montado.checagem} · {nomeRepetido ? 'nome repetido' : 'nome único'} · tamanho {tamanhoLegivel(bytes)}{tamanhoOk ? '' : ' (passa de 256 KB)'}
          {montado.erros.length > 0 && ` — ${montado.erros[0]}`}
        </span>
        <span className="texto-mudo">
          {atual ? `Editado por ${atual.alteradoPor || 'admin'} ${formatar.idade(atual.alteradoEm)}` : 'Ainda não salvo'} · {estado === 'salvando' ? 'salvando…' : sujo ? 'alterações por salvar' : 'salvo automaticamente'}
        </span>
      </footer>
    </section>
  );
}
