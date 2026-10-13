import { useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { VersaoDoDevKit } from '../api/tipos';
import { formatar } from '../formatar';

/** O que o link sabe da última versão: ainda perguntando, a versão, ou indisponível (o 503 da API). */
type Estado = { tipo: 'carregando' } | { tipo: 'pronta'; versao: VersaoDoDevKit } | { tipo: 'indisponivel' };

/**
 * O "Baixar o dev.kit" da landing (US #405). Lê a última versão em `GET /api/versoes/ultima` e aponta para o zip
 * pela API (o proxy da GitHub Release: o visitante nunca vê o GitHub nem o token dele). Desde a US #425 o download
 * é ANÔNIMO — a reversão do login da US #405: quem baixa começa o teste grátis de 14 dias na primeira abertura, e
 * a API limita os downloads por IP.
 *
 * Desde a US #467 (task #471) é um LINK de verdade, e não mais um botão com fetch e blob: o navegador mostra o
 * endereço no hover, o "Copiar endereço do link" funciona e a URL, escrita abaixo, cola na barra de qualquer
 * navegador. O endereço é o alias permanente `/api/versoes/ultima/download`, que continua valendo quando sair a
 * próxima versão. O `rotulo` troca o texto (o "Testar grátis por 14 dias" dos planos); `discreto` é o link de
 * texto do hero, abaixo dos dois botões da proposta.
 */
export function BaixarDevKit({ rotulo, discreto = false }: { rotulo?: string; discreto?: boolean } = {}) {
  const [estado, setEstado] = useState<Estado>({ tipo: 'carregando' });

  useEffect(() => {
    let vivo = true;
    api.ultimaVersao()
      .then((versao) => vivo && setEstado({ tipo: 'pronta', versao }))
      .catch(() => vivo && setEstado({ tipo: 'indisponivel' }));
    return () => {
      vivo = false;
    };
  }, []);

  const grupo = rotulo ? `Download do dev.kit: ${rotulo}` : 'Download do dev.kit';
  const classe = discreto ? 'link-baixar' : 'botao botao-escuro cta';

  if (estado.tipo !== 'pronta') {
    return (
      <div className={`baixar${discreto ? ' baixar-discreto' : ''}`} role="group" aria-label={grupo}>
        {discreto
          ? <span className={`${classe} desabilitado`} aria-disabled="true">{rotulo ?? 'Baixar o dev.kit'}</span>
          : <button className={classe} type="button" disabled>{rotulo ?? 'Baixar o dev.kit'}</button>}
        <span className="baixar-nota">{estado.tipo === 'carregando' ? 'Consultando a última versão…' : 'Download indisponível no momento.'}</span>
      </div>
    );
  }

  const { versao } = estado;
  const endereco = api.enderecoDoDownload();
  return (
    <div className={`baixar${discreto ? ' baixar-discreto' : ''}`} role="group" aria-label={grupo}>
      <a className={classe} href={endereco}>
        {rotulo ?? `Baixar o ${formatar.versaoDoDevKit(versao.versao)}`}
      </a>
      <span className="baixar-nota">
        versão {versao.versao} · {formatar.data(versao.publicadaEm)} · {formatar.megabytes(versao.tamanhoBytes)} · 14 dias grátis, sem login
      </span>
      <span className="baixar-endereco">
        Ou copie o endereço: <code>{endereco}</code>
      </span>
    </div>
  );
}
