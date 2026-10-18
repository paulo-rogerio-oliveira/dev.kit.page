import { useState } from 'react';
import type { ConvencaoDaEmpresa, NivelDeConvencao } from '../api/tipos';
import { EditorDeContexto, ehContexto } from './EditorDeContexto';

/** Um usuário que o admin geral pode escolher para editar o contexto (o login vai na gravação; o nome, na leitura). */
export interface UsuarioDoContexto {
  login: string;
  nome: string;
}

/** Grava um tema no nível (vazio remove); o usuário (login) só vale para o admin geral. */
export type GravarContexto = (nivel: NivelDeConvencao, chave: string, valor: string, time: string, usuario: string) => Promise<boolean>;

const normalizarEquipe = (texto: string) => texto.trim().toLowerCase();

/** Como a auditoria nomeia o dono do contexto de nível usuário: o nome, ou o login (ConhecimentoDaEmpresa.ParaLeituraAsync). */
const nomeDe = (u: UsuarioDoContexto) => u.nome || u.login;

/**
 * O contexto do dev.kit de uma empresa nos três níveis (US #550) — a aba Contexto de /empresa/conhecimento (o gestor e
 * o dev) e o painel Contexto do admin em /empresas. Empresa e equipe: quem `podeGerir` (o gestor, o admin) edita; o dev
 * só lê. Usuários: com `usuarios` (o admin), escolhe-se um e edita-se o contexto dele; sem, a lista é só leitura — cada
 * um edita o próprio em /conta. A composição no dev.kit é empresa → equipe → usuário, e em conflito vale o mais
 * específico.
 */
export function ContextoPorNivel({ convencoes, equipes, podeGerir, usuarios, aoGravar }: {
  convencoes: ConvencaoDaEmpresa[];
  /** As equipes conhecidas da empresa (GET .../equipes). */
  equipes: string[];
  podeGerir: boolean;
  usuarios?: UsuarioDoContexto[];
  aoGravar: GravarContexto;
}) {
  const contexto = convencoes.filter((c) => ehContexto(c.chave));
  const conhecidas = [...new Set([...equipes, ...contexto.filter((c) => c.nivel === 'time').map((c) => c.time)])].sort();
  const [equipe, setEquipe] = useState(conhecidas[0] ?? '');
  const [usuario, setUsuario] = useState(usuarios?.[0]?.login ?? '');
  const daEquipe = normalizarEquipe(equipe);
  const escolhido = usuarios?.find((u) => u.login === usuario);

  const doNivel = (nivel: NivelDeConvencao, filtro: (c: ConvencaoDaEmpresa) => boolean = () => true) =>
    contexto.filter((c) => c.nivel === nivel && filtro(c));
  const donos = [...new Set(doNivel('usuario').map((c) => c.usuario))].sort();

  return (
    <div className="secoes-do-contexto">
      <section aria-labelledby="titulo-contexto-empresa">
        <h3 id="titulo-contexto-empresa">Empresa</h3>
        <p className="nota-do-formulario">Vale para todos os agentes da empresa.</p>
        <EditorDeContexto rotulo="Contexto da empresa" itens={doNivel('empresa')} editavel={podeGerir}
          aoGravar={(chave, valor) => aoGravar('empresa', chave, valor, '', '')} vazio="A empresa ainda não definiu contexto." />
      </section>

      <section aria-labelledby="titulo-contexto-equipe">
        <h3 id="titulo-contexto-equipe">Equipe</h3>
        <label className="campo-da-equipe">
          Equipe
          <input aria-label="Equipe" value={equipe} onChange={(e) => setEquipe(e.target.value)} list="equipes-conhecidas" maxLength={80}
            placeholder="A Equipe das Configurações do dev.kit" />
          <datalist id="equipes-conhecidas">
            {conhecidas.map((t) => <option key={t} value={t} />)}
          </datalist>
        </label>
        <p className="nota-do-formulario">Vale para quem declara esta equipe nas Configurações do dev.kit, por cima do da empresa.</p>
        {daEquipe ? (
          <EditorDeContexto rotulo={`Contexto da equipe ${daEquipe}`} itens={doNivel('time', (c) => c.time === daEquipe)} editavel={podeGerir}
            aoGravar={(chave, valor) => aoGravar('time', chave, valor, daEquipe, '')} vazio="Esta equipe ainda não tem contexto." />
        ) : (
          <p className="vazio">Escolha ou digite uma equipe.</p>
        )}
      </section>

      <section aria-labelledby="titulo-contexto-usuarios">
        <h3 id="titulo-contexto-usuarios">Usuários</h3>
        {usuarios ? (
          <>
            <label>
              Usuário
              <select aria-label="Usuário" value={usuario} onChange={(e) => setUsuario(e.target.value)}>
                {usuarios.map((u) => <option key={u.login} value={u.login}>{nomeDe(u)}{u.nome ? ` (${u.login})` : ''}</option>)}
              </select>
            </label>
            {escolhido ? (
              <EditorDeContexto rotulo={`Contexto de ${nomeDe(escolhido)}`} itens={doNivel('usuario', (c) => c.usuario === nomeDe(escolhido))} editavel
                aoGravar={(chave, valor) => aoGravar('usuario', chave, valor, '', escolhido.login)} vazio="Este usuário ainda não tem contexto." />
            ) : (
              <p className="vazio">A empresa ainda não tem usuários.</p>
            )}
          </>
        ) : (
          <>
            <p className="nota-do-formulario">Cada um cadastra o próprio em Conta → Meu contexto; aqui, só a leitura.</p>
            {donos.length === 0 ? (
              <p className="vazio">Nenhum usuário definiu contexto.</p>
            ) : (
              donos.map((dono) => (
                <div key={dono}>
                  <h4>{dono}</h4>
                  <EditorDeContexto rotulo={`Contexto de ${dono}`} itens={doNivel('usuario', (c) => c.usuario === dono)} editavel={false} aoGravar={async () => false} />
                </div>
              ))
            )}
          </>
        )}
      </section>
    </div>
  );
}
