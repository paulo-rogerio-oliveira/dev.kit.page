import { useState, type FormEvent } from 'react';
import { ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ParticipanteDeEquipe } from '../api/tipos';
import { formatar } from '../formatar';
import { PainelLateral } from './PainelLateral';
import { Selo } from './Selo';
import type { UsuarioDoContexto } from './EditorDeContexto';

/**
 * Quem participa de cada equipe (US #556) — do gestor da empresa e do admin do board, que DECIDEM: o dev só pede (a
 * Equipe das Configurações do dev.kit chega na sincronização como pedido pendente). Aprovar dá o acesso ao contexto da
 * equipe; recusar (ou remover) o tira. O formulário inclui alguém direto, sem esperar o pedido.
 */
export function ParticipantesDaEquipe({ participantes, usuarios, aoDecidir, aoMudar, aoExpirar }: {
  participantes: ParticipanteDeEquipe[];
  usuarios: UsuarioDoContexto[];
  aoDecidir: (equipe: string, usuarioId: number, situacao: 'aprovado' | 'recusado') => Promise<unknown>;
  aoMudar: () => Promise<unknown>;
  aoExpirar: () => void;
}) {
  const [usuarioId, setUsuarioId] = useState<number | ''>('');
  const [equipe, setEquipe] = useState('');
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');
  const pendentes = participantes.filter((p) => p.situacao === 'pendente').length;

  async function decidir(alvoEquipe: string, alvo: number, situacao: 'aprovado' | 'recusado', mensagem: string) {
    setErro('');
    setAviso('');
    try {
      await aoDecidir(alvoEquipe, alvo, situacao);
      setAviso(mensagem);
      await aoMudar();
      return true;
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) aoExpirar();
      else setErro(mensagemDaFalha(falha));
      return false;
    }
  }

  async function incluir(evento: FormEvent) {
    evento.preventDefault();
    if (usuarioId === '') return;
    const nome = usuarios.find((u) => u.id === usuarioId)?.nome ?? '';
    if (await decidir(equipe.trim(), usuarioId, 'aprovado', `${nome} incluído na equipe ${equipe.trim().toLowerCase()}.`)) {
      setUsuarioId('');
      setEquipe('');
    }
  }

  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Participantes das equipes">
        <h2 className="titulo-do-cartao">Participantes das equipes {pendentes > 0 && <Selo variante="aviso">{pendentes} aguardando</Selo>}</h2>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        {participantes.length === 0 ? (
          <p className="vazio">Ninguém pediu para participar de uma equipe ainda — o pedido chega quando o dev.kit do dev sincroniza com a Equipe preenchida.</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr><th>Equipe</th><th>Usuário</th><th>Situação</th><th>Desde</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {participantes.map((p) => (
                  <tr key={`${p.equipe}|${p.usuarioId}`}>
                    <td>{p.equipe}</td>
                    <td>{p.usuario || '—'}</td>
                    <td><Selo variante={p.situacao === 'aprovado' ? 'sucesso' : 'aviso'} ponto>{p.situacao === 'aprovado' ? 'Aprovado' : 'Pendente'}</Selo></td>
                    <td>{formatar.dataHora(p.atualizadoEm)}</td>
                    <td>
                      <span className="acoes">
                        {p.situacao === 'pendente' && (
                          <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Aprovar ${p.usuario} em ${p.equipe}`}
                            onClick={() => void decidir(p.equipe, p.usuarioId, 'aprovado', `${p.usuario} aprovado na equipe ${p.equipe}.`)}>Aprovar</button>
                        )}
                        <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`${p.situacao === 'pendente' ? 'Recusar' : 'Remover'} ${p.usuario} de ${p.equipe}`}
                          onClick={() => void decidir(p.equipe, p.usuarioId, 'recusado', `${p.usuario} fora da equipe ${p.equipe}.`)}>
                          {p.situacao === 'pendente' ? 'Recusar' : 'Remover'}
                        </button>
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PainelLateral id="titulo-incluir-participante" titulo="Incluir numa equipe">
        <form className="formulario-lateral" onSubmit={(e) => void incluir(e)} aria-label="Incluir numa equipe">
          <label>
            Usuário
            <select value={usuarioId} onChange={(e) => setUsuarioId(e.target.value ? Number(e.target.value) : '')} required>
              <option value="">Escolha o usuário</option>
              {usuarios.map((u) => <option key={u.id} value={u.id}>{u.nome}</option>)}
            </select>
          </label>
          <label>
            Equipe
            <input value={equipe} onChange={(e) => setEquipe(e.target.value)} maxLength={80} placeholder="vendas" required />
          </label>
          <p className="nota-do-formulario">Quem participa lê e grava o contexto da equipe; o pedido pendente não dá acesso a nada.</p>
          <button className="botao botao-primario botao-largo" type="submit" disabled={usuarioId === '' || !equipe.trim()}>Incluir</button>
        </form>
      </PainelLateral>
    </div>
  );
}
