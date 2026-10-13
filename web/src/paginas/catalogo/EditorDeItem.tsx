import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react';
import { api } from '../../api/cliente';
import type { ItemEditavel, TipoDeItem } from '../../api/tipos';
import {
  lerFluxo, lerPlano, lerSkill, montarPlano, montarSkill, PLANO_VAZIO, SKILL_VAZIA, textoDoFluxo, validarPlano, validarSkill,
  type CamposDaSkill, type CamposDoPlano,
} from './conteudo';
import { NOME_DO_TIPO } from './tipos';

/** O título do editor de um item novo (o gênero muda com o tipo). */
const NOVO: Record<TipoDeItem, string> = { skill: 'Nova skill', plan: 'Novo tech plan', fluxo: 'Novo fluxo' };

/** Uma marcação (checkbox) do formulário, com o texto do que ela muda no dev.kit. */
function Marcacao({ rotulo, valor, aoMudar }: { rotulo: string; valor: boolean; aoMudar: (valor: boolean) => void }) {
  return (
    <label className="marcacao">
      <input type="checkbox" checked={valor} onChange={(e) => aoMudar(e.target.checked)} />
      {rotulo}
    </label>
  );
}

/**
 * O editor de um item do catálogo (US #461). Skill e Tech Plan são formulários com os campos do editor do dev.kit
 * desktop, e o que vai para a API é o JSON que o app grava (`montarSkill`/`montarPlano`, na mesma ordem de campos).
 * O fluxo é o envelope `devkit-fluxo` como texto — o desenho gráfico continua no dev.kit —, com a conferência local
 * e a prévia dos passos. Salvar grava RASCUNHO; quem leva às máquinas é o Publicar.
 */
export function EditorDeItem({ token, slug, tipo, item, aoSalvar, aoCancelar, tratar }: {
  token: string;
  slug: string;
  tipo: TipoDeItem;
  item: ItemEditavel | null;
  aoSalvar: (item: ItemEditavel) => void;
  aoCancelar: () => void;
  tratar: (falha: unknown) => string;
}) {
  const [carregando, setCarregando] = useState(item !== null);
  const [skill, setSkill] = useState<CamposDaSkill>(SKILL_VAZIA);
  const [plano, setPlano] = useState<CamposDoPlano>(PLANO_VAZIO);
  const [fluxo, setFluxo] = useState('');
  const [categoria, setCategoria] = useState(item?.categoria ?? '');
  const [erros, setErros] = useState<string[]>([]);
  const [salvando, setSalvando] = useState(false);

  // Editar: o conteúdo só vem no detalhe do item (a lista o traz nulo).
  useEffect(() => {
    if (!item) return;
    let vivo = true;
    api.itemDoCatalogo(token, slug, item.id)
      .then((detalhe) => {
        if (!vivo) return;
        if (tipo === 'skill') setSkill(lerSkill(detalhe.conteudo));
        else if (tipo === 'plan') setPlano(lerPlano(detalhe.conteudo));
        else setFluxo(textoDoFluxo(detalhe.conteudo));
        setCategoria(detalhe.categoria ?? '');
      })
      .catch((falha) => vivo && setErros([tratar(falha)]))
      .finally(() => vivo && setCarregando(false));
    return () => {
      vivo = false;
    };
  }, [item, token, slug, tipo, tratar]);

  const lido = tipo === 'fluxo' ? lerFluxo(fluxo) : null;

  async function carregarArquivo(evento: ChangeEvent<HTMLInputElement>) {
    const arquivo = evento.target.files?.[0];
    if (!arquivo) return;
    setFluxo(await arquivo.text());
    setErros([]);
    evento.target.value = '';
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    const problemas = tipo === 'skill' ? validarSkill(skill) : tipo === 'plan' ? validarPlano(plano) : lido!.erros;
    setErros(problemas);
    if (problemas.length > 0) return;

    const conteudo = tipo === 'skill' ? montarSkill(skill) : tipo === 'plan' ? montarPlano(plano) : fluxo.trim();
    const corpo = { tipo, conteudo, ...(categoria.trim() ? { categoria: categoria.trim() } : {}) };
    setSalvando(true);
    try {
      aoSalvar(item ? await api.editarItem(token, slug, item.id, corpo) : await api.criarItem(token, slug, corpo));
    } catch (falha) {
      setErros([tratar(falha)]);
    } finally {
      setSalvando(false);
    }
  }

  const titulo = item ? `Editar ${NOME_DO_TIPO[tipo].toLowerCase()} ${item.nome}` : NOVO[tipo];

  return (
    <section className="cartao editor-de-item" aria-labelledby="titulo-do-editor">
      <h2 id="titulo-do-editor" className="titulo-do-cartao">{titulo}</h2>
      {carregando ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : (
        <form className="formulario-do-editor" onSubmit={salvar} aria-label={titulo} noValidate>
          {tipo === 'skill' && (
            <>
              <div className="campos-lado-a-lado">
                <label>
                  Nome
                  <input className="fonte-mono" value={skill.name} onChange={(e) => setSkill({ ...skill, name: e.target.value })} placeholder="compilar-angular" maxLength={100} />
                </label>
                <label>
                  Categoria
                  <input value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="vazia: derivada do nome" maxLength={50} />
                </label>
              </div>
              <label>
                Descrição
                <input value={skill.description} onChange={(e) => setSkill({ ...skill, description: e.target.value })} maxLength={500} />
              </label>
              <label>
                Conteúdo
                <textarea className="fonte-mono" rows={14} value={skill.content} onChange={(e) => setSkill({ ...skill, content: e.target.value })} />
              </label>
              <div className="campos-lado-a-lado">
                <label>
                  Argumentos (um por linha)
                  <textarea className="fonte-mono" rows={3} value={skill.arguments} onChange={(e) => setSkill({ ...skill, arguments: e.target.value })} />
                </label>
                <label>
                  Repositórios (um por linha)
                  <textarea className="fonte-mono" rows={3} value={skill.repositories} onChange={(e) => setSkill({ ...skill, repositories: e.target.value })} />
                </label>
              </div>
              <div className="marcacoes">
                <Marcacao rotulo="É comando (/nome no chat)" valor={skill.isCommand} aoMudar={(v) => setSkill({ ...skill, isCommand: v })} />
                <Marcacao rotulo="Roda em segundo plano" valor={skill.isBackground} aoMudar={(v) => setSkill({ ...skill, isBackground: v })} />
                <Marcacao rotulo="Fixada no combo da conversa" valor={skill.isPinned} aoMudar={(v) => setSkill({ ...skill, isPinned: v })} />
              </div>
              <details className="json-montado">
                <summary>Ver o JSON que vai para o dev.kit</summary>
                <pre className="trace" aria-label="JSON da skill">{montarSkill(skill)}</pre>
              </details>
            </>
          )}

          {tipo === 'plan' && (
            <>
              <div className="campos-lado-a-lado">
                <label>
                  Chave
                  <input className="fonte-mono" value={plano.key} onChange={(e) => setPlano({ ...plano, key: e.target.value })} placeholder="vazia: derivada da descrição" maxLength={100} />
                </label>
                <label>
                  Categoria
                  <input value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="vazia: derivada da chave" maxLength={50} />
                </label>
              </div>
              <label>
                Descrição
                <input value={plano.description} onChange={(e) => setPlano({ ...plano, description: e.target.value })} maxLength={500} />
              </label>
              <label>
                Conteúdo
                <textarea className="fonte-mono" rows={14} value={plano.content} onChange={(e) => setPlano({ ...plano, content: e.target.value })} />
              </label>
              <label>
                Repositório (vazio: todos)
                <input value={plano.repository} onChange={(e) => setPlano({ ...plano, repository: e.target.value })} maxLength={200} />
              </label>
              <div className="marcacoes">
                <Marcacao rotulo="Só com work item" valor={plano.onlyWithWorkItem} aoMudar={(v) => setPlano({ ...plano, onlyWithWorkItem: v })} />
                <Marcacao rotulo="Obrigatório" valor={plano.isRequired} aoMudar={(v) => setPlano({ ...plano, isRequired: v })} />
                <Marcacao rotulo="Instrução do agente (abre o prompt)" valor={plano.isAgentInstruction} aoMudar={(v) => setPlano({ ...plano, isAgentInstruction: v })} />
              </div>
              <details className="json-montado">
                <summary>Ver o JSON que vai para o dev.kit</summary>
                <pre className="trace" aria-label="JSON do tech plan">{montarPlano(plano)}</pre>
              </details>
            </>
          )}

          {tipo === 'fluxo' && lido && (
            <>
              <p className="nota-da-secao">
                O desenho gráfico do fluxo continua no dev.kit desktop. Exporte-o com <code>devcli fluxos --exportar</code> e carregue
                aqui o arquivo .json (ou cole o JSON) — as skills que as etapas usam têm de estar neste catálogo ou no básico.
              </p>
              <div className="campos-lado-a-lado">
                <label>
                  Carregar arquivo .json
                  <input type="file" accept=".json,application/json" onChange={(e) => void carregarArquivo(e)} />
                </label>
                <label>
                  Categoria
                  <input value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="vazia: derivada do nome" maxLength={50} />
                </label>
              </div>
              <label>
                JSON do fluxo
                <textarea className="fonte-mono" rows={16} value={fluxo} spellCheck={false} onChange={(e) => setFluxo(e.target.value)} />
              </label>
              {lido.erros.length === 0 && (
                <div className="previa-do-fluxo">
                  <h3>{lido.nome}</h3>
                  {lido.descricao && <p className="nota-da-secao">{lido.descricao}</p>}
                  <ol aria-label="Prévia dos passos">
                    {lido.passos.map((p) => (
                      <li key={p.id}><code>{p.id}</code> → {p.kind}{p.skillName ? <> → <code>{p.skillName}</code></> : null}</li>
                    ))}
                  </ol>
                  {lido.planos > 0 && <p className="texto-mudo">{lido.planos} Tech Plan(s) embutido(s) vão junto com o fluxo.</p>}
                </div>
              )}
            </>
          )}

          {erros.length > 0 && (
            <ul className="erro lista-de-erros" role="alert">
              {erros.map((e) => <li key={e}>{e}</li>)}
            </ul>
          )}
          <div className="acoes">
            <button className="botao botao-primario" type="submit" disabled={salvando}>Salvar rascunho</button>
            <button className="botao botao-fantasma" type="button" onClick={aoCancelar}>Cancelar</button>
          </div>
        </form>
      )}
    </section>
  );
}
