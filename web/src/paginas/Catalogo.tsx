import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CatalogoNomeado, Vitrine as VitrineDoCatalogo } from '../api/tipos';
import { Abas } from '../componentes/Abas';
import { Pagina } from '../componentes/Pagina';
import { MenuDoAdmin, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { papelDa, useSessao } from '../sessao';
import { Catalogos } from './catalogo/Catalogos';
import { Releases } from './catalogo/Releases';
import { itensDaVitrine, Vitrine } from './catalogo/Vitrine';

type Estado = { tipo: 'carregando' } | { tipo: 'pronta'; vitrine: VitrineDoCatalogo } | { tipo: 'indisponivel' };

/** As abas do catálogo. A aba e o catálogo escolhido ficam na URL (`?aba=releases&catalogo=slug`): o link volta ao mesmo lugar. */
export type AbaDoCatalogo = 'vitrine' | 'releases' | 'catalogos';

/** O catálogo de cada aba quando a URL não diz: o básico. */
const BASICO = 'basico';

/**
 * O catálogo do dev.kit (US #425; abas da US #461, /catalogo). A **Vitrine** é PÚBLICA: as skills, os Tech Plans e
 * os fluxos do catálogo básico, de `GET /api/catalogo/base/vitrine`. O admin ganha as abas **Releases** (a antiga
 * /catalogo-base, agora de qualquer catálogo) e **Catálogos** (cadastro, empresas, edição dos itens e Publicar).
 * Quem não é admin vê só a vitrine, qualquer que seja a aba da URL.
 */
export function Catalogo() {
  const { sessao, sair } = useSessao();
  const ehAdmin = !!sessao && !sessao.deveTrocarSenha && papelDa(sessao) === 'admin';
  const [parametros, setParametros] = useSearchParams();
  const [estado, setEstado] = useState<Estado>({ tipo: 'carregando' });
  const [catalogos, setCatalogos] = useState<CatalogoNomeado[] | null>(null);
  const [erro, setErro] = useState('');

  const pedida = parametros.get('aba');
  const aba: AbaDoCatalogo = ehAdmin && (pedida === 'releases' || pedida === 'catalogos') ? pedida : 'vitrine';
  const slug = parametros.get('catalogo');

  const irPara = useCallback((novaAba: AbaDoCatalogo, catalogo: string | null = null) => {
    const novos: Record<string, string> = {};
    if (novaAba !== 'vitrine') novos.aba = novaAba;
    if (catalogo) novos.catalogo = catalogo;
    setParametros(novos);
  }, [setParametros]);

  const expirou = useCallback(() => sair('expirou'), [sair]);

  /** A falha das abas do admin: o 401 encerra a sessão; o resto vira a mensagem da API (o `detail`). */
  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      expirou();
      return '';
    }
    return mensagemDaFalha(falha);
  }, [expirou]);

  const carregarCatalogos = useCallback(
    () => (sessao ? api.catalogos(sessao.token).then(setCatalogos).catch((falha) => setErro(tratar(falha))) : Promise.resolve()),
    [sessao, tratar],
  );

  useEffect(() => {
    let vivo = true;
    api.vitrine()
      .then((vitrine) => vivo && setEstado({ tipo: 'pronta', vitrine }))
      .catch(() => vivo && setEstado({ tipo: 'indisponivel' }));
    return () => {
      vivo = false;
    };
  }, []);

  useEffect(() => {
    if (ehAdmin) void carregarCatalogos();
  }, [ehAdmin, carregarCatalogos]);

  const quantos = estado.tipo === 'pronta' ? itensDaVitrine(estado.vitrine) : 0;
  const pilula = estado.tipo === 'pronta' && (
    <p className="pilula">
      <span className="pilula-ponto" aria-hidden="true" />
      Versão {estado.vitrine.versao} · publicada em {formatar.data(estado.vitrine.publicadaEm)} · {quantos} {quantos === 1 ? 'item' : 'itens'}
    </p>
  );

  const vitrine = (
    <>
      {estado.tipo === 'carregando' && <p className="carregando" role="status">Carregando…</p>}
      {estado.tipo === 'indisponivel' && <p className="aviso" role="status">O catálogo está indisponível no momento. Tente de novo em alguns minutos.</p>}
      {estado.tipo === 'pronta' && <Vitrine vitrine={estado.vitrine} />}
    </>
  );

  return (
    <div className="pagina">
      <Topo secao="catálogo">{ehAdmin && <MenuDoAdmin />}</Topo>
      <Pagina
        rotulo="Catálogo"
        titulo="Catálogo do dev.kit"
        subtitulo="Skills, tech plans e fluxos que vêm com o dev.kit. Chegam sozinhos a cada máquina quando sai uma versão nova."
        acao={pilula}
      >
        {!ehAdmin ? vitrine : (
          <Abas
            rotulo="Seções do catálogo"
            prefixo="catalogo"
            ativa={aba}
            aoMudar={(id) => irPara(id as AbaDoCatalogo, id === 'vitrine' ? null : slug)}
            abas={[{ id: 'vitrine', rotulo: 'Vitrine' }, { id: 'releases', rotulo: 'Releases' }, { id: 'catalogos', rotulo: 'Catálogos' }]}
          >
            {erro && <p className="erro" role="alert">{erro}</p>}
            {aba === 'vitrine' && vitrine}
            {aba === 'releases' && (
              <Releases
                token={sessao!.token}
                slug={slug ?? BASICO}
                catalogos={catalogos ?? []}
                aoMudarCatalogo={(novo) => irPara('releases', novo)}
                aoExpirar={expirou}
              />
            )}
            {aba === 'catalogos' && (
              <Catalogos
                token={sessao!.token}
                catalogos={catalogos}
                aberto={slug}
                aoAbrir={(aberto) => irPara('catalogos', aberto)}
                aoMudarLista={carregarCatalogos}
                aoPublicar={(publicado) => {
                  void carregarCatalogos();
                  irPara('releases', publicado);
                }}
                tratar={tratar}
              />
            )}
          </Abas>
        )}
      </Pagina>
    </div>
  );
}
