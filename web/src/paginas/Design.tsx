import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { ProjetoDeDesign } from '../api/tipos';
import { branchCurto, DesignCompartilhado, falhaDoDesign, ImagemDaVersao } from '../componentes/DesignCompartilhado';
import { Pagina } from '../componentes/Pagina';
import { Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** O tratamento de falha das páginas de design: o 401 volta ao login; o resto vira a mensagem. */
function useTratar(setErro: (texto: string) => void) {
  const { sair } = useSessao();
  return useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(falhaDoDesign(falha));
  }, [sair, setErro]);
}

/**
 * O design de quem NÃO tem empresa (US #669, /design): o mesmo componente da aba Design de /empresa/conhecimento, no
 * nível usuário — o design de quem trabalha sozinho também sai da máquina. Quem tem empresa também chega aqui (o link
 * da conta), com os escopos.
 */
export function Design() {
  const { sessao } = useSessao();
  const [erro, setErro] = useState('');
  const tratar = useTratar(setErro);
  return (
    <div className="pagina">
      <Topo secao="design">
        <Link className="botao botao-fantasma" to="/projetos">Projetos</Link>
        <Link className="botao botao-fantasma" to="/conta">Conta</Link>
      </Topo>
      <Pagina rotulo={sessao!.empresa ?? 'Design'} titulo="Design" subtitulo="As capturas e as propostas de tela do dev.kit, por repositório.">
        {erro && <p className="erro" role="alert">{erro}</p>}
        <DesignCompartilhado token={sessao!.token} comEmpresa={Boolean(sessao!.empresa)} tratar={tratar} />
      </Pagina>
    </div>
  );
}

/**
 * A página do LINK ESTÁVEL de uma versão (US #669, /design/{projeto}/{tela}/v{n}) — o que a PR da semana cita no lugar
 * de anexar a imagem ao git: a imagem, de onde ela saiu (branch @ commit), quem a gravou e a base. Exige login; quem não
 * é do escopo do projeto recebe a mensagem do 404.
 */
export function VersaoDoDesign() {
  const { sessao } = useSessao();
  const { projeto: idDoProjeto, tela: idDaTela, versao: rotuloDaVersao } = useParams();
  const [projeto, setProjeto] = useState<ProjetoDeDesign | null>(null);
  const [erro, setErro] = useState('');
  const tratar = useTratar(setErro);
  const numero = Number((rotuloDaVersao ?? '').replace(/^v/i, ''));

  useEffect(() => {
    api.projetoDeDesign(sessao!.token, Number(idDoProjeto)).then(setProjeto).catch(tratar);
  }, [sessao, idDoProjeto, tratar]);

  const tela = projeto?.telas.find((t) => t.id === idDaTela);
  const versao = tela?.versoes.find((v) => v.numero === numero);
  return (
    <div className="pagina">
      <Topo secao="design">
        <Link className="botao botao-fantasma" to="/design">Design</Link>
      </Topo>
      <Pagina rotulo={projeto?.chave ?? 'Design'} titulo={tela ? `${tela.nome} · v${String(numero).padStart(3, '0')}` : 'Versão de design'}
        subtitulo={versao ? `${versao.tipo === 'proposta' ? 'Proposta visual — nada implementado' : 'Captura da tela real'} · ${versao.largura}x${versao.altura} · ${versao.tema}` : undefined}>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {!projeto && !erro && <p className="carregando" role="status">Carregando…</p>}
        {projeto && !versao && <p className="erro" role="alert">{falhaDoDesign(new ErroDaApi(404, ''))}</p>}
        {versao && (
          <section className="cartao design-pagina-da-versao" aria-label="Versão de design">
            <p className="design-meta">
              <code>{branchCurto(versao.branch) || '—'}{versao.commit ? ` @ ${versao.commit}` : ''}</code> · {versao.origem} ·{' '}
              {versao.autorTipo === 'agente' ? `agente${versao.runner ? ` (${versao.runner})` : ''}` : versao.autor} · {formatar.dataHora(versao.criadaEm)}
              {versao.base ? ` · base v${String(versao.base).padStart(3, '0')}` : ''}
            </p>
            {versao.descricao && <p>{versao.descricao}</p>}
            <ImagemDaVersao token={sessao!.token} versao={versao} tratar={tratar} />
          </section>
        )}
      </Pagina>
    </div>
  );
}
