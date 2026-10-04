import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

/**
 * A chamada para ação: primária (a cor da marca) ou secundária. Um destino com '#' é uma âncora da
 * própria página; o resto é uma rota do app (o login, por exemplo).
 */
export function Cta({ para, variante = 'primario', children }: { para: string; variante?: 'primario' | 'secundario'; children: ReactNode }) {
  const classe = `botao ${variante === 'primario' ? 'botao-primario' : 'botao-fantasma'} cta`;
  return para.startsWith('#')
    ? <a className={classe} href={para}>{children}</a>
    : <Link className={classe} to={para}>{children}</Link>;
}
