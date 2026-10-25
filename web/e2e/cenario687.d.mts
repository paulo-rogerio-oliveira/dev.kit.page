// Os tipos do cenário da US #687 (cenario687.mjs), para o us687.spec.ts compilar no tsc da web.

/** AAAA-MM-DD de hoje + dias (UTC). */
export function diaIso(dias?: number): string;

/** O token do admin: com a senha definitiva, ou trocando a inicial por ela. */
export function tokenDoAdmin(api: string, senhaInicial: string, novaSenha: string): Promise<string>;

/** O conteúdo da skill que o editor mostra. */
export const SKILL_NET: string;

/** O que o cenário criou. */
export interface Cenario687 {
  acme: { id: number; nome: string };
  globex: { id: number; nome: string };
  ana: { id: number; login: string };
  bruno: { id: number; login: string };
  devDaAcme: { id: number; login: string };
  maquinaId: string;
  itemNet?: { id: number; nome: string };
}

/** Monta o cenário pela API: empresas, usuários com a licença, a telemetria do painel e o catálogo com um rascunho. */
export function montarCenario687(api: string, token: string, codigoDeRegistro: string, marca?: string): Promise<Cenario687>;
