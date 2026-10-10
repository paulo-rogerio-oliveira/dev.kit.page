import { useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { TipoDeItem, Vitrine } from '../api/tipos';
import { Topo } from '../componentes/Topo';
import { formatar } from '../formatar';

/** O nome de cada tipo de item na vitrine. */
export const NOME_DO_TIPO: Record<TipoDeItem, string> = { skill: 'Skill', plan: 'Tech Plan', fluxo: 'Fluxo' };

type Estado = { tipo: 'carregando' } | { tipo: 'pronta'; vitrine: Vitrine } | { tipo: 'indisponivel' };

/**
 * A vitrine PÚBLICA do catálogo básico (US #425, /catalogo): as skills, os Tech Plans e os fluxos que vêm com o
 * dev.kit, agrupados por tecnologia — lidos de `GET /api/catalogo/base/vitrine`, o manifesto da última release do
 * catálogo. Sem login: é a página que mostra o que o agente sabe fazer.
 */
export function Catalogo() {
  const [estado, setEstado] = useState<Estado>({ tipo: 'carregando' });

  useEffect(() => {
    let vivo = true;
    api.vitrine()
      .then((vitrine) => vivo && setEstado({ tipo: 'pronta', vitrine }))
      .catch(() => vivo && setEstado({ tipo: 'indisponivel' }));
    return () => {
      vivo = false;
    };
  }, []);

  return (
    <div className="pagina">
      <Topo secao="catálogo" />
      <main className="painel">
        <section aria-labelledby="titulo-catalogo">
          <h1 id="titulo-catalogo">Catálogo do dev.kit</h1>
          <p className="nota-da-secao">
            As skills, os Tech Plans e os fluxos que vêm com o dev.kit — e chegam sozinhos a cada máquina quando uma versão nova é publicada.
          </p>
          {estado.tipo === 'carregando' && <p className="carregando" role="status">Carregando…</p>}
          {estado.tipo === 'indisponivel' && <p className="aviso" role="status">O catálogo está indisponível no momento. Tente de novo em alguns minutos.</p>}
          {estado.tipo === 'pronta' && (
            <>
              <p className="nota-da-secao">Versão {estado.vitrine.versao} do catálogo, publicada em {formatar.data(estado.vitrine.publicadaEm)}.</p>
              {estado.vitrine.grupos.map((grupo) => (
                <section key={grupo.tecnologia} aria-labelledby={`tecnologia-${grupo.tecnologia}`} className="grupo-do-catalogo">
                  <h2 id={`tecnologia-${grupo.tecnologia}`}>{grupo.tecnologia}</h2>
                  <ul className="grade-cartoes">
                    {grupo.itens.map((item) => (
                      <li key={`${item.tipo}:${item.nome}`} className="cartao">
                        <span className="estado tipo-do-item">{NOME_DO_TIPO[item.tipo] ?? item.tipo}</span>
                        <h3>{item.nome}</h3>
                        <p>{item.descricao}</p>
                        <small>versão {item.versao}</small>
                      </li>
                    ))}
                  </ul>
                </section>
              ))}
            </>
          )}
        </section>
      </main>
    </div>
  );
}
