import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { VersaoDoDevKit } from '../api/tipos';
import { salvarArquivo } from '../arquivos';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** O que o botão sabe da última versão: ainda perguntando, a versão, ou indisponível (o 503 da API). */
type Estado = { tipo: 'carregando' } | { tipo: 'pronta'; versao: VersaoDoDevKit } | { tipo: 'indisponivel' };

/**
 * O "Baixar o dev.kit" da landing (US #405). Lê a última versão em `GET /api/versoes/ultima` (anônima) e
 * mostra o número; o download exige o usuário: sem sessão, leva ao login (que volta para cá); com ela, baixa
 * por `fetch` com o Bearer e entrega o blob — um link direto para a rota não levaria o token, e o zip vem
 * da API (proxy da GitHub Release): o visitante nunca vê o GitHub nem o token dele.
 */
export function BaixarDevKit() {
  const { sessao, sair } = useSessao();
  const navegar = useNavigate();
  const [estado, setEstado] = useState<Estado>({ tipo: 'carregando' });
  const [baixando, setBaixando] = useState(false);
  const [erro, setErro] = useState('');

  useEffect(() => {
    let vivo = true;
    api.ultimaVersao()
      .then((versao) => vivo && setEstado({ tipo: 'pronta', versao }))
      .catch(() => vivo && setEstado({ tipo: 'indisponivel' }));
    return () => {
      vivo = false;
    };
  }, []);

  async function baixar(versao: VersaoDoDevKit) {
    setErro('');
    if (!sessao) {
      navegar('/login', { state: { de: '/' } });
      return;
    }
    if (sessao.deveTrocarSenha) {
      navegar('/trocar-senha');
      return;
    }
    setBaixando(true);
    try {
      salvarArquivo(await api.baixarVersao(sessao.token, versao));
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) {
        sair('expirou');
        navegar('/login', { state: { de: '/' } });
        return;
      }
      setErro(falha instanceof ErroDaApi && falha.status === 403
        ? 'O seu usuário não pode baixar o dev.kit. Fale com o administrador.'
        : 'Não foi possível baixar agora. Tente de novo em alguns minutos.');
    } finally {
      setBaixando(false);
    }
  }

  if (estado.tipo !== 'pronta') {
    return (
      <div className="baixar" role="group" aria-label="Download do dev.kit">
        <button className="botao botao-escuro cta" type="button" disabled>Baixar o dev.kit</button>
        <span className="baixar-nota">{estado.tipo === 'carregando' ? 'Consultando a última versão…' : 'Download indisponível no momento.'}</span>
      </div>
    );
  }

  const { versao } = estado;
  return (
    <div className="baixar" role="group" aria-label="Download do dev.kit">
      <button className="botao botao-escuro cta" type="button" disabled={baixando} onClick={() => void baixar(versao)}>
        {baixando ? 'Baixando…' : `Baixar o dev.kit ${versao.versao}`}
      </button>
      <span className="baixar-nota">
        versão {versao.versao} · {formatar.data(versao.publicadaEm)} · {formatar.megabytes(versao.tamanhoBytes)}
        {!sessao && ' · entre para baixar'}
      </span>
      {erro && <span className="erro" role="alert">{erro}</span>}
    </div>
  );
}
