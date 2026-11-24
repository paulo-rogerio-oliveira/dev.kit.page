import { webcrypto } from 'node:crypto';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { identidadeDoDesign, resolverProposta } from './PropostasDoPlano';
import type { ProjetoDeDesign, VersaoDeDesign } from '../api/tipos';

beforeEach(() => vi.stubGlobal('crypto', webcrypto));
afterEach(() => vi.unstubAllGlobals());
const identidade = 'github-com-paulo-rogerio-oliveira-git-kit-55a8ebea5417f232';
const referencia = { projeto: identidade, tela: 'plano', versao: 6, hash: 'a'.repeat(64) };
const versao: VersaoDeDesign = { numero: 6, tipo: 'proposta', base: 1, tema: 'dark', largura: 1440, altura: 900,
  origem: 'proposta', alvo: '', branch: 'feature/dev/projeto', commit: 'abc123', descricao: '', autor: 'Demo',
  autorTipo: 'agente', runner: 'codex', criadaEm: '2026-11-18T10:00:00Z', sha256: referencia.hash, bytes: 1,
  link: '/api/design/42/plano/v6', podeApagar: false };
const projeto: ProjetoDeDesign = { id: 42, repositorio: 'https://github.com/paulo-rogerio-oliveira/git.kit',
  chave: 'github.com/paulo-rogerio-oliveira/git.kit', nivel: 'usuario', time: '', dono: 'Demo', atualizadoEm: '',
  telas: [{ id: 'plano', nome: 'Plano', estado: 'proposto', versoes: [versao, { ...versao, numero: 7, sha256: 'b'.repeat(64) }] }] };

describe('Propostas fixadas no planejamento', () => {
  it('usa a identidade portátil do desktop e preserva a versão anterior à mais recente', async () => {
    expect(await identidadeDoDesign(projeto.chave)).toBe(identidade);
    const resolvida = await resolverProposta(referencia, [projeto]);
    expect(resolvida.projeto).toBe(42);
    expect(resolvida.versao?.numero).toBe(6);
    expect(resolvida.erro).toBeUndefined();
  });
  it('mantém a referência e apresenta pendência para ausência, hash divergente e escopo ambíguo', async () => {
    expect((await resolverProposta(referencia, [])).erro).toContain('indisponível');
    const divergente = await resolverProposta({ ...referencia, hash: 'b'.repeat(64) }, [projeto]);
    expect(divergente.versao).toBeUndefined();
    expect(divergente.erro).toContain('hash divergente');
    expect((await resolverProposta(referencia, [projeto, { ...projeto, id: 43 }])).erro).toContain('ambígua');
    expect(referencia.versao).toBe(6);
  });
});
