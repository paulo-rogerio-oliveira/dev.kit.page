import { useState, type FormEvent } from 'react';
import type { ConvencaoDaEmpresa } from '../api/tipos';
import { formatar } from '../formatar';

/** O contexto do dev.kit (US #550) é a família de chaves contexto.<tema> das convenções da empresa. */
export const PREFIXO_DO_CONTEXTO = 'contexto.';

/** O teto do texto de um tema — o mesmo da API (RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoValorDeContexto). */
export const TETO_DO_CONTEXTO = 4000;

/** A chave é do contexto, e não de uma convenção como a do branch. */
export const ehContexto = (chave: string) => chave.trim().toLowerCase().startsWith(PREFIXO_DO_CONTEXTO);

/** O tema da chave (contexto.stack → stack). */
export const temaDa = (chave: string) => chave.slice(PREFIXO_DO_CONTEXTO.length);

/** O tema como a chave aceita: minúsculas, sem acento, só letras, números, ponto, hífen e sublinhado. */
const normalizarTema = (texto: string) =>
  texto.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, '-').replace(/[^a-z0-9._-]/g, '');

/**
 * O editor do contexto de UM nível (US #550) — usado em /conta (o do usuário), na aba Contexto da empresa (a empresa e
 * cada equipe) e no painel do admin (todos os níveis, inclusive o de um usuário). Lista os temas com quem gravou e
 * quando; com `editavel`, edita (o mesmo tema troca o texto), grava um tema novo e remove (remover = gravar vazio,
 * como no PUT da API). Quem grava é a página (`aoGravar`), que conhece a rota e recarrega a lista.
 */
export function EditorDeContexto({ rotulo, itens, editavel, aoGravar, vazio }: {
  /** O nome do nível, para os rótulos ("Contexto da empresa"). */
  rotulo: string;
  /** Os temas JÁ do nível (as chaves contexto.*). */
  itens: ConvencaoDaEmpresa[];
  editavel: boolean;
  /** Grava o texto do tema (vazio remove); devolve se deu certo. */
  aoGravar: (chave: string, valor: string) => Promise<boolean>;
  vazio?: string;
}) {
  const [tema, setTema] = useState('');
  const [texto, setTexto] = useState('');
  const temas = itens.filter((c) => ehContexto(c.chave));

  async function gravar(evento: FormEvent) {
    evento.preventDefault();
    if (await aoGravar(`${PREFIXO_DO_CONTEXTO}${tema}`, texto.trim())) {
      setTema('');
      setTexto('');
    }
  }

  async function remover(c: ConvencaoDaEmpresa) {
    if (!window.confirm(`Remover o tema ${temaDa(c.chave)} do ${rotulo.toLowerCase()}?`)) return;
    await aoGravar(c.chave, '');
  }

  return (
    <div className="editor-de-contexto">
      {temas.length === 0 ? (
        <p className="vazio">{vazio ?? 'Nenhum tema neste nível.'}</p>
      ) : (
        <ul className="lista-de-contexto" aria-label={rotulo}>
          {temas.map((c) => (
            <li key={c.chave}>
              <div className="cabecalho-do-tema">
                <code className="codigo">{temaDa(c.chave)}</code>
                <small>{[c.autor, c.atualizadoEm ? formatar.dataHora(c.atualizadoEm) : ''].filter(Boolean).join(' · ')}</small>
                {editavel && (
                  <span className="acoes">
                    <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar o tema ${temaDa(c.chave)}`}
                      onClick={() => { setTema(temaDa(c.chave)); setTexto(c.valor); }}>Editar</button>
                    <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Remover o tema ${temaDa(c.chave)}`}
                      onClick={() => void remover(c)}>Remover</button>
                  </span>
                )}
              </div>
              <p className="texto-do-contexto">{c.valor}</p>
            </li>
          ))}
        </ul>
      )}

      {editavel && (
        <form className="formulario-lateral" onSubmit={(e) => void gravar(e)} aria-label={`Gravar no ${rotulo.toLowerCase()}`}>
          <label>
            Tema
            <input aria-label="Tema" value={tema} onChange={(e) => setTema(normalizarTema(e.target.value))} maxLength={50} placeholder="stack, revisao, estilo" required />
          </label>
          <label>
            Texto
            <textarea aria-label="Texto" value={texto} onChange={(e) => setTexto(e.target.value)} maxLength={TETO_DO_CONTEXTO} rows={6}
              placeholder="O que os agentes devem saber: tecnologia, padrões, como revisar, o que evitar" required />
          </label>
          <p className="nota-do-formulario contador" aria-live="polite">{texto.length} / {TETO_DO_CONTEXTO} caracteres. O mesmo tema troca o texto.</p>
          <button className="botao botao-primario botao-largo" type="submit" disabled={!tema || !texto.trim()}>Gravar</button>
        </form>
      )}
    </div>
  );
}
