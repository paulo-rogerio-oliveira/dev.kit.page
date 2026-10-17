import { useCallback, useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { EmpresaResumo, UsuarioResumo, VisaoDoContexto } from '../api/tipos';
import { EditorDeContexto } from '../componentes/EditorDeContexto';
import { Pagina } from '../componentes/Pagina';
import { ParticipantesDaEquipe } from '../componentes/ParticipantesDaEquipe';
import { MenuDoAdmin, Topo } from '../componentes/Topo';
import { useSessao } from '../sessao';

/**
 * O contexto de QUALQUER empresa para o admin do board dev.kit (US #556, /admin/contexto): escolhe a empresa e vê e
 * altera tudo — o da empresa, o de cada equipe, o pessoal de cada usuário e as sugestões dos agentes —, e decide quem
 * participa das equipes. As rotas são as do admin (/api/admin/empresas/{id}/contexto), sobre o MESMO serviço das outras
 * visões.
 */
export function AdminContexto() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [empresas, setEmpresas] = useState<EmpresaResumo[] | null>(null);
  const [usuarios, setUsuarios] = useState<UsuarioResumo[]>([]);
  const [empresaId, setEmpresaId] = useState<number | null>(null);
  const [visao, setVisao] = useState<VisaoDoContexto | null>(null);
  const [erro, setErro] = useState('');

  const expirar = useCallback(() => sair('expirou'), [sair]);
  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) expirar();
    else setErro(mensagemDaFalha(falha));
  }, [expirar]);

  useEffect(() => {
    Promise.all([api.empresas(token), api.usuarios(token)])
      .then(([lista, todos]) => {
        setEmpresas(lista);
        setUsuarios(todos);
        setEmpresaId((atual) => atual ?? lista[0]?.id ?? null);
      })
      .catch(tratar);
  }, [token, tratar]);

  const carregar = useCallback(async () => {
    if (empresaId === null) return;
    try {
      setVisao(await api.visaoDoContexto(token, empresaId));
    } catch (falha) {
      tratar(falha);
    }
  }, [token, empresaId, tratar]);

  useEffect(() => {
    setVisao(null);
    void carregar();
  }, [carregar]);

  const daEmpresa = usuarios.filter((u) => u.empresaId === empresaId).map((u) => ({ id: u.id, nome: u.nome || u.login }));

  return (
    <div className="pagina">
      <Topo secao="contexto"><MenuDoAdmin /></Topo>
      <Pagina
        titulo="Contexto das empresas"
        subtitulo="O admin do board vê e altera o contexto de qualquer empresa: o dela, o das equipes, o de cada usuário e as sugestões dos agentes."
        acao={(
          <label className="seletor-de-empresa">
            Empresa
            <select value={empresaId ?? ''} onChange={(e) => setEmpresaId(Number(e.target.value))} disabled={!empresas?.length}>
              {(empresas ?? []).map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
            </select>
          </label>
        )}
      >
        {erro && <p className="erro" role="alert">{erro}</p>}
        {empresas && empresas.length === 0 && <p className="vazio">Nenhuma empresa cadastrada.</p>}
        {empresaId !== null && (
          <>
            <EditorDeContexto
              key={empresaId}
              itens={visao?.itens ?? null}
              niveis={['empresa', 'equipe', 'usuario']}
              equipes={visao?.equipes ?? []}
              podeEditar={() => true}
              usuarios={daEmpresa}
              aoGravar={(g) => api.gravarContexto(token, g, empresaId)}
              aoExcluir={(i) => api.excluirContexto(token, i.id, empresaId)}
              aoPromover={(i) => api.promoverContexto(token, i.id, empresaId)}
              aoMudar={carregar}
              aoExpirar={expirar}
            />
            {visao && (
              <ParticipantesDaEquipe
                participantes={visao.participantes}
                usuarios={daEmpresa}
                aoDecidir={(equipe, usuarioId, situacao) => api.decidirParticipacao(token, equipe, usuarioId, situacao, empresaId)}
                aoMudar={carregar}
                aoExpirar={expirar}
              />
            )}
          </>
        )}
      </Pagina>
    </div>
  );
}
