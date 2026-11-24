import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

/**
 * A chamada para ação: primária (a cor da marca), secundária ou de texto (o link dos cartões da proposta
 * ajustes-2-landing v1, "Conhecer os recursos →"). Um destino com '#' é uma âncora da própria página; o resto é uma
 * rota do app (o login, por exemplo).
 */
export function Cta({ para, variante = 'primario', children }: { para: string; variante?: 'primario' | 'secundario' | 'texto'; children: ReactNode }) {
  const classe = variante === 'texto' ? 'link-da-chamada' : `botao ${variante === 'primario' ? 'botao-primario' : 'botao-fantasma'} cta`;
  return para.startsWith('#')
    ? <a className={classe} href={para}>{children}</a>
    : <Link className={classe} to={para}>{children}</Link>;
}
