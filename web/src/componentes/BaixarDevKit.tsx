import { useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { VersaoDoDevKit } from '../api/tipos';
import { salvarArquivo } from '../arquivos';
import { formatar } from '../formatar';

/** O que o botão sabe da última versão: ainda perguntando, a versão, ou indisponível (o 503 da API). */
type Estado = { tipo: 'carregando' } | { tipo: 'pronta'; versao: VersaoDoDevKit } | { tipo: 'indisponivel' };

/**
 * O "Baixar o dev.kit" da landing (US #405). Lê a última versão em `GET /api/versoes/ultima` e baixa o zip pela
 * API (o proxy da GitHub Release: o visitante nunca vê o GitHub nem o token dele). Desde a US #425 o download é
 * ANÔNIMO — a reversão do login da US #405: quem baixa começa o teste grátis de 14 dias na primeira abertura, e
 * a API limita os downloads por IP. O `rotulo` troca o texto do botão (o "Testar grátis por 14 dias" dos planos).
 */
export function BaixarDevKit({ rotulo }: { rotulo?: string } = {}) {
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
    setBaixando(true);
    try {
      salvarArquivo(await api.baixarVersao(versao));
    } catch {
      setErro('Não foi possível baixar agora. Tente de novo em alguns minutos.');
    } finally {
      setBaixando(false);
    }
  }

  if (estado.tipo !== 'pronta') {
    return (
      <div className="baixar" role="group" aria-label={rotulo ? `Download do dev.kit: ${rotulo}` : 'Download do dev.kit'}>
        <button className="botao botao-escuro cta" type="button" disabled>{rotulo ?? 'Baixar o dev.kit'}</button>
        <span className="baixar-nota">{estado.tipo === 'carregando' ? 'Consultando a última versão…' : 'Download indisponível no momento.'}</span>
      </div>
    );
  }

  const { versao } = estado;
  return (
    <div className="baixar" role="group" aria-label={rotulo ? `Download do dev.kit: ${rotulo}` : 'Download do dev.kit'}>
      <button className="botao botao-escuro cta" type="button" disabled={baixando} onClick={() => void baixar(versao)}>
        {baixando ? 'Baixando…' : rotulo ?? `Baixar o dev.kit ${versao.versao}`}
      </button>
      <span className="baixar-nota">
        versão {versao.versao} · {formatar.data(versao.publicadaEm)} · {formatar.megabytes(versao.tamanhoBytes)} · 14 dias grátis, sem login
      </span>
      {erro && <span className="erro" role="alert">{erro}</span>}
    </div>
  );
}
