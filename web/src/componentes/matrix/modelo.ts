import type { CelulaMatrix, DocumentoMatrix, EdicaoMatrix, MatrizMatrix, RegistroMatrix, RevisaoMatrix } from '../../api/matrix';

export const igual = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);
export const ultimo = <T,>(lista: T[]): T | undefined => lista[lista.length - 1];
export const identificador = (s: string) => /^[a-zA-Z0-9_.-]{1,100}$/.test(s);

/** Dois parágrafos legíveis no campo de texto v1; clientes anteriores continuam lendo a obrigação e seu motivo. */
export function lerDefinicao(texto: string): { obrigacao: string; motivo: string } {
  const separador = '\n\nMotivo: ';
  if (texto.startsWith('Obrigação: ') && texto.includes(separador)) {
    const i = texto.lastIndexOf(separador);
    return { obrigacao: texto.slice('Obrigação: '.length, i), motivo: texto.slice(i + separador.length) };
  }
  return { obrigacao: texto, motivo: texto };
}
export const escreverDefinicao = (obrigacao: string, motivo: string) => `Obrigação: ${obrigacao}\n\nMotivo: ${motivo}`;

/** A mesma comparação do contrato: renomear eixo também afeta as células que o usam. */
export function celulasAlteradas(a: MatrizMatrix | null | undefined, b: MatrizMatrix | null | undefined): string[] {
  const antes = a?.celulas ?? [], depois = b?.celulas ?? [];
  const eixos = (campo: 'componentes' | 'layers') => [...(a?.[campo] ?? []), ...(b?.[campo] ?? [])]
    .filter(e => !igual(a?.[campo].find(x => x.id === e.id), b?.[campo].find(x => x.id === e.id))).map(e => e.id);
  const componentes = eixos('componentes'), layers = eixos('layers');
  return [...new Set([...antes, ...depois].map(c => c.id))].filter(id =>
    !igual(antes.find(c => c.id === id), depois.find(c => c.id === id)) ||
    [...antes, ...depois].some(c => c.id === id && (componentes.includes(c.componente) || layers.includes(c.layer))));
}

export function registroVigente(r: RegistroMatrix, historico: RevisaoMatrix[]): boolean {
  const origem = historico.find(h => h.versao === r.revisaoDaDefinicao), atual = ultimo(historico);
  return !!origem && !!atual && !celulasAlteradas(origem.documento.matriz, atual.documento.matriz).includes(r.celula);
}

/** Só reaplica o que mudou localmente; preserva adições e alterações remotas não tocadas. */
export function reaplicar(base: DocumentoMatrix, local: DocumentoMatrix, remoto: DocumentoMatrix): DocumentoMatrix {
  if (base.aplicacao !== remoto.aplicacao || base.id !== remoto.id || base.tipo !== remoto.tipo) throw new Error('Alvos diferentes.');
  function lista<T extends { id: string }>(a: T[], b: T[], c: T[]) {
    const alterados = new Set([...a, ...b].filter(x => !igual(a.find(e => e.id === x.id), b.find(e => e.id === x.id))).map(x => x.id));
    return [...c.filter(x => !alterados.has(x.id)), ...b.filter(x => alterados.has(x.id))];
  }
  if (base.matriz && local.matriz && remoto.matriz) return { ...remoto, matriz: {
    ...remoto.matriz,
    revisaoDoMapa: base.matriz.revisaoDoMapa === local.matriz.revisaoDoMapa ? remoto.matriz.revisaoDoMapa : local.matriz.revisaoDoMapa,
    componentes: lista(base.matriz.componentes, local.matriz.componentes, remoto.matriz.componentes),
    layers: lista(base.matriz.layers, local.matriz.layers, remoto.matriz.layers),
    celulas: lista(base.matriz.celulas, local.matriz.celulas, remoto.matriz.celulas),
  } };
  if (base.mapa && local.mapa && remoto.mapa) return { ...remoto, mapa: {
    nome: base.mapa.nome === local.mapa.nome ? remoto.mapa.nome : local.mapa.nome,
    repositorios: igual(base.mapa.repositorios, local.mapa.repositorios) ? remoto.mapa.repositorios : local.mapa.repositorios,
    componentes: lista(base.mapa.componentes, local.mapa.componentes, remoto.mapa.componentes),
    layers: lista(base.mapa.layers, local.mapa.layers, remoto.mapa.layers),
    relacoes: lista(base.mapa.relacoes, local.mapa.relacoes, remoto.mapa.relacoes),
  } };
  return { ...local };
}

export function validarCelula(c: CelulaMatrix): string {
  const definicao = lerDefinicao(c.justificativa);
  if (!definicao.obrigacao.trim() || !definicao.motivo.trim()) return 'Informe a obrigação concreta e a justificativa. Não aplicação exige motivo.';
  if (![c.justificativa, c.origem, c.criterio, c.evidenciaEsperada].every(t => t.trim())) return 'Informe obrigação e justificativa, origem, critério e evidência esperada. Não aplicação exige motivo.';
  if (c.aplicavel && !c.responsaveis.length) return 'Atribua pelo menos uma task responsável à célula aplicável.';
  if (c.responsaveis.some(r => !identificador(r.id))) return 'Use um identificador estável para cada responsável (letras, números, ponto, hífen ou sublinhado).';
  return '';
}

export interface RascunhoMatrix { edicao: EdicaoMatrix; base: DocumentoMatrix }
// Somente na aba e separado por conta/empresa/alvo. Não guarda token ou credenciais.
export const chaveRascunho = (conta: string, alvo: DocumentoMatrix) =>
  `matrix-v1:${conta}:${alvo.aplicacao}:${alvo.tipo}:${alvo.id}`;
export function lerRascunho(chave: string): RascunhoMatrix | null {
  try {
    const d = JSON.parse(sessionStorage.getItem(chave) ?? 'null') as RascunhoMatrix | null;
    return d?.edicao.documento.contrato === 1 && d.base?.contrato === 1 ? d : null;
  } catch { return null; }
}
export function exportarRascunho(r: RascunhoMatrix) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(r, null, 2)], { type: 'application/json' }));
  const a = document.createElement('a'); a.href = url; a.download = `matrix-${r.edicao.documento.id}-rascunho.json`; a.click();
  URL.revokeObjectURL(url);
}
