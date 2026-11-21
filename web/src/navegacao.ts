import type { LoginResponse, Papel } from './api/tipos';

/** Destinos e acesso compartilhados pelo menu, pelas rotas e pelo retorno do login. */
export const destinos = [
  { caminho: '/board', nome: 'Board' },
  { caminho: '/projetos', nome: 'Projetos' },
  { caminho: '/design', nome: 'Design' },
  { caminho: '/catalogo', nome: 'Catálogo' },
  { caminho: '/empresa/conhecimento', nome: 'Repositórios e convenções', papeis: ['gestor', 'dev'] },
  { caminho: '/dashboard', nome: 'Uso', papeis: ['admin', 'gestor'] },
  { caminho: '/empresas', nome: 'Empresas', papeis: ['admin'] },
  { caminho: '/usuarios', nome: 'Usuários', papeis: ['admin'] },
  { caminho: '/empresa/usuarios', nome: 'Usuários da empresa', papeis: ['gestor'] },
  { caminho: '/empresa/catalogo', nome: 'Catálogo da empresa', papeis: ['gestor'] },
  { caminho: '/relatos', nome: 'Relatos', papeis: ['admin', 'gestor'] },
  { caminho: '/conta', nome: 'Minha conta' },
] as const;

export function papelDaNavegacao(sessao: LoginResponse): Papel {
  return sessao.papel ?? (sessao.ehAdmin ? 'admin' : 'gestor');
}
export function inicioDaSessao(sessao: LoginResponse): string {
  return papelDaNavegacao(sessao) === 'dev' ? '/conta' : '/dashboard';
}
export function destinoDoCaminho(caminho: string) {
  return destinos.find(d => d.caminho === caminho || (d.caminho === '/design' && /^\/design\/[^/]+\/[^/]+\/v?\d+$/i.test(caminho)));
}
export function podeAbrir(sessao: LoginResponse, caminho: string): boolean {
  const destino = destinoDoCaminho(caminho);
  if (destino) return !('papeis' in destino) || (destino.papeis as readonly string[]).includes(papelDaNavegacao(sessao));
  return ['/', '/aderir', '/definir-senha', '/comprar', '/compra-concluida', '/catalogo-base'].includes(caminho);
}

/** Aceita somente rotas conhecidas, sem normalização para outro host ou rota. */
export function caminhoInterno(valor: unknown): string | null {
  if (typeof valor !== 'string' || valor.length > 4096 || !valor.startsWith('/') || valor.startsWith('//')) return null;
  try {
    const decodificado = decodeURIComponent(valor);
    if (/[\\\u0000-\u0020\u007f]/.test(decodificado.split(/[?#]/)[0]) || decodificado.startsWith('//')) return null;
    const url = new URL(valor, 'https://devkit.local');
    if (url.origin !== 'https://devkit.local' || url.pathname !== valor.split(/[?#]/)[0]) return null;
    return url.pathname;
  } catch { return null; }
}
export function destinoSeguro(sessao: LoginResponse, valor: unknown): string | null {
  const caminho = caminhoInterno(valor);
  return caminho && podeAbrir(sessao, caminho) ? valor as string : null;
}

/** Retorno determinístico, sem depender do histórico de outra aba ou site. */
export function retornoSeguro(sessao: LoginResponse, valor: unknown, atual: string): string | null {
  const seguro = destinoSeguro(sessao, valor);
  const caminho = caminhoInterno(seguro);
  if (!seguro || !caminho || caminho === atual || !destinoDoCaminho(caminho)) return null;
  const visitados = new Set([atual]);
  let proximo: string | null = seguro;
  for (let nivel = 0; proximo; nivel++) {
    const rota = caminhoInterno(proximo);
    if (nivel > 2 || !rota || visitados.has(rota) || !destinoSeguro(sessao, proximo)) return null;
    visitados.add(rota);
    proximo = new URL(proximo, 'https://devkit.local').searchParams.get('retorno');
  }
  return seguro;
}
export function comRetorno(destino: string, origem: string): string {
  const url = new URL(destino, 'https://devkit.local');
  url.searchParams.set('retorno', origem);
  return url.pathname + url.search + url.hash;
}
export function listaDeProjetos(consulta: URLSearchParams): string {
  const q = new URLSearchParams(consulta);
  ['projeto', 'slug', 'visao', 'retorno'].forEach(chave => q.delete(chave));
  return `/projetos${q.size ? `?${q}` : ''}`;
}
