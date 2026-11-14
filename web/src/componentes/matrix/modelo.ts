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
  const coberturas = [...(a?.coberturas ?? []), ...(b?.coberturas ?? [])]
    .filter(c => !(a?.coberturas ?? []).some(x => igual(x, c)) || !(b?.coberturas ?? []).some(x => igual(x, c))).map(c => c.celula);
  return [...new Set([...antes, ...depois].map(c => c.id))].filter(id =>
    coberturas.includes(id) || !igual(antes.find(c => c.id === id), depois.find(c => c.id === id)) ||
    [...antes, ...depois].some(c => c.id === id && (componentes.includes(c.componente) || layers.includes(c.layer))));
}

export function registroVigente(r: RegistroMatrix, historico: RevisaoMatrix[]): boolean {
  return !motivoDaObsolescencia(r, historico);
}

export function motivoDaObsolescencia(r: RegistroMatrix, historico: RevisaoMatrix[]): string {
  if (r.tipo === 'avaliacao' && !r.evento) return 'Avaliação anterior sem vínculo verificável ao evento do host.';
  let antes = historico.find(h => h.versao === r.revisaoDaDefinicao);
  if (!antes) return 'Revisão da definição indisponível.';
  for (const depois of historico.filter(h => h.versao > r.revisaoDaDefinicao).slice().sort((a, b) => a.versao - b.versao)) {
    if (celulasAlteradas(antes.documento.matriz, depois.documento.matriz).includes(r.celula))
      return `Obrigação ou atribuição da célula alterada na revisão ${depois.versao}.`;
    antes = depois;
  }
  return '';
}

export function estadoDaCelula(c: CelulaMatrix, registros: RevisaoMatrix[], historico: RevisaoMatrix[]): string {
  const lista = new Map<string, RegistroMatrix>();
  registros.slice().sort((a, b) => a.versao - b.versao).forEach(x => {
    const r = x.documento.registro;
    if (r?.tipo === 'avaliacao' && r.celula === c.id && c.responsaveis.some(d => d.id === r.responsavel))
      lista.set(`${r.responsavel}:${r.avaliador ?? 0}`, r);
  });
  const todos = [...lista.values()];
  const resultados = todos.filter(r => r.evento || !todos.some(v => v.responsavel === r.responsavel && v.evento));
  if (!resultados.length) return registros.some(x => x.documento.registro?.celula === c.id) ? 'A avaliar' : 'A implementar';
  if (resultados.some(r => !registroVigente(r, historico))) return 'A avaliar';
  if (c.responsaveis.some(d => !resultados.some(r => r.responsavel === d.id))) return 'A avaliar';
  return c.aplicavel && resultados.every(r => r.resultado === 'atendido') ? 'Comprovado' : 'A avaliar';
}

const objeto = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
/** Mescla em três vias por campo. Colisões no mesmo campo usam o valor local após a comparação explícita. */
function mesclar(base: unknown, local: unknown, remoto: unknown): unknown {
  if (igual(base, local)) return remoto;
  if (igual(base, remoto) || igual(local, remoto)) return local;
  if (Array.isArray(base) && Array.isArray(local) && Array.isArray(remoto)) {
    const todos = [...base, ...local, ...remoto];
    if (todos.every(v => objeto(v) && typeof v.id === 'string')) {
      const antes = new Map(base.map(v => [v.id, v]));
      const depois = new Map(local.map(v => [v.id, v]));
      const servidor = new Map(remoto.map(v => [v.id, v]));
      return [...new Set([...servidor.keys(), ...depois.keys()])].flatMap(id => {
        if (antes.has(id) && !depois.has(id)) return []; // Desativação local explícita.
        if (!depois.has(id)) return [servidor.get(id)];
        if (!servidor.has(id)) return igual(antes.get(id), depois.get(id)) ? [] : [depois.get(id)];
        return [mesclar(antes.get(id), depois.get(id), servidor.get(id))];
      });
    }
    // Listas de IDs: conserva inclusões remotas e aplica apenas inclusões/remoções locais.
    return [...remoto.filter(v => !base.some(b => igual(b, v)) || local.some(l => igual(l, v))),
      ...local.filter(v => !base.some(b => igual(b, v)) && !remoto.some(r => igual(r, v)))];
  }
  if (objeto(base) && objeto(local) && objeto(remoto)) {
    return Object.fromEntries([...new Set([...Object.keys(base), ...Object.keys(local), ...Object.keys(remoto)])]
      .map(k => [k, mesclar(base[k], local[k], remoto[k])]));
  }
  return local;
}

/** Só reaplica campos alterados localmente, preservando os demais campos e inclusões do servidor. */
export function reaplicar(base: DocumentoMatrix, local: DocumentoMatrix, remoto: DocumentoMatrix): DocumentoMatrix {
  if (base.aplicacao !== remoto.aplicacao || base.id !== remoto.id || base.tipo !== remoto.tipo) throw new Error('Alvos diferentes.');
  return mesclar(base, local, remoto) as DocumentoMatrix;
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
