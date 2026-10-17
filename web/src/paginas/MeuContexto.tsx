import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { NivelDeContexto, UsuarioDaEmpresa, VisaoDoContexto } from '../api/tipos';
import { EditorDeContexto } from '../componentes/EditorDeContexto';
import { Pagina } from '../componentes/Pagina';
import { ParticipantesDaEquipe } from '../componentes/ParticipantesDaEquipe';
import { Topo } from '../componentes/Topo';
import { papelDa, useSessao } from '../sessao';

/**
 * O CONTEXTO do dev.kit na landing de quem é da empresa (US #556, /contexto): o que os agentes e os avaliadores
 * consideram. O DEV vê o dele, o das equipes de que participa (aprovado) e o da empresa (só leitura) — e as equipes que
 * pediu e ainda aguardam o gestor. O GESTOR (o admin da empresa) também edita o da empresa e o de todas as equipes, e
 * decide quem participa de cada uma. A permissão é da API: a tela só esconde o que ela recusaria.
 */
export function MeuContexto() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const ehGestor = papelDa(sessao!) === 'gestor';
  const [visao, setVisao] = useState<VisaoDoContexto | null>(null);
  const [usuarios, setUsuarios] = useState<UsuarioDaEmpresa[]>([]);
  const [erro, setErro] = useState('');

  const expirar = useCallback(() => sair('expirou'), [sair]);

  const carregar = useCallback(async () => {
    try {
      setVisao(await api.visaoDoContexto(token));
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) expirar();
      else setErro(mensagemDaFalha(falha));
    }
  }, [token, expirar]);

  useEffect(() => {
    void carregar();
    // O gestor inclui alguém direto numa equipe: a lista dos usuários da empresa.
    if (ehGestor) api.usuariosDaEmpresa(token).then(setUsuarios).catch(() => setUsuarios([]));
  }, [carregar, ehGestor, token]);

  const niveis: NivelDeContexto[] = visao?.editaEmpresa ? ['usuario', 'equipe', 'empresa'] : (visao?.equipes.length ?? 0) > 0 ? ['usuario', 'equipe'] : ['usuario'];

  return (
    <div className="pagina">
      <Topo secao="contexto">
        {ehGestor ? <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link> : <Link className="botao botao-fantasma" to="/conta">Conta</Link>}
        <Link className="botao botao-fantasma" to="/empresa/conhecimento">Repositórios</Link>
      </Topo>
      <Pagina
        titulo="Contexto do dev.kit"
        subtitulo="O que os agentes e os avaliadores consideram ao decidir: as INSTRUÇÕES (escritas por pessoas) valem como regra; as SUGESTÕES (anotadas pelos agentes ao fim de uma execução) são opcionais."
      >
        {erro && <p className="erro" role="alert">{erro}</p>}
        {visao && visao.equipesPendentes.length > 0 && (
          <p className="nota-informativa" role="status">
            Aguardando a aprovação do gestor para a equipe {visao.equipesPendentes.join(', ')}: o contexto dela aparece aqui depois de aprovado.
          </p>
        )}
        <EditorDeContexto
          key={niveis.join('|')}
          itens={visao?.itens ?? null}
          niveis={niveis}
          equipes={visao?.equipes ?? []}
          podeEditar={(i) => i.nivel !== 'empresa' || (visao?.editaEmpresa ?? false)}
          aoGravar={(g) => api.gravarContexto(token, g)}
          aoExcluir={(i) => api.excluirContexto(token, i.id)}
          aoPromover={(i) => api.promoverContexto(token, i.id)}
          aoMudar={carregar}
          aoExpirar={expirar}
        />
        {ehGestor && visao && (
          <ParticipantesDaEquipe
            participantes={visao.participantes}
            usuarios={usuarios.filter((u) => u.papel === 'dev').map((u) => ({ id: u.id, nome: u.nome || u.login }))}
            aoDecidir={(equipe, usuarioId, situacao) => api.decidirParticipacao(token, equipe, usuarioId, situacao)}
            aoMudar={carregar}
            aoExpirar={expirar}
          />
        )}
      </Pagina>
    </div>
  );
}
