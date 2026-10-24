import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { vi } from 'vitest';
import type { VersaoDeDesign } from '../api/tipos';
import { entregues } from '../testes/navegador';
import { renderizar } from '../testes/renderizar';
import { API, escritas, projetosDeDesign, servidor, tokenDoDev, tokenDoGestor } from '../testes/servidor';
import { comparacao, papelDaUltima } from './DesignCompartilhado';

afterEach(() => vi.restoreAllMocks());

const v = (numero: number, tipo: 'captura' | 'proposta', base: number | null = null) =>
  ({ ...projetosDeDesign[0].telas[0].versoes[0], numero, tipo, base }) as VersaoDeDesign;

describe('Design compartilhado (US #669)', () => {
  it('a comparação é a captura de antes, a última proposta e a recaptura depois dela', () => {
    const lado = comparacao([v(1, 'captura'), v(2, 'captura'), v(3, 'proposta', 1), v(4, 'captura', 3), v(5, 'captura', 3)]);
    expect(lado.map((c) => [c.versao.numero, c.papel, c.legenda])).toEqual([[1, 'captura', 'antes'], [3, 'proposta', 'base v001'], [5, 'recaptura', 'depois']]);
    expect(comparacao([v(1, 'captura')]).map((c) => c.papel)).toEqual(['captura']);
    expect(comparacao([v(1, 'proposta')]).map((c) => c.legenda)).toEqual(['tela nova']);
    expect(papelDaUltima([v(1, 'captura'), v(2, 'proposta', 1), v(3, 'captura')])).toBe('recaptura');
    expect(papelDaUltima([v(1, 'captura')])).toBe('captura');
  });

  it('a aba Design mostra o repositório, as telas e a captura | proposta | recaptura lado a lado, com o uso do teto', async () => {
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());

    expect(await screen.findByRole('tab', { name: 'Design', selected: true })).toBeInTheDocument();
    expect(await screen.findByText('37 de 500 versões · 48 MB de 2 GB — plano Empresarial')).toBeInTheDocument();
    expect(screen.getByLabelText('Repositório')).toHaveValue('github.com/acme/portal');
    const telas = within(screen.getByRole('region', { name: 'Telas' }));
    expect(telas.getByRole('button', { name: /Checkout.*recaptura.*v003 · 3 versões · semana5/ })).toBeInTheDocument();
    expect(telas.getByRole('button', { name: /Login.*captura.*v001 · 1 versão · semana3/ })).toBeInTheDocument();

    const comparacaoNaTela = screen.getByRole('region', { name: 'Versões de Checkout' });
    const colunas = within(comparacaoNaTela).getAllByRole('article');
    expect(colunas.map((c) => c.getAttribute('aria-label'))).toEqual(['v001 captura', 'v002 proposta', 'v003 recaptura']);
    expect(colunas[0]).toHaveTextContent(/antes.*semana4 @ 81c0e2d · web · light/);
    expect(colunas[1]).toHaveTextContent(/base v001.*Botão de pagar em destaque\./);
    expect(await within(colunas[2]).findByRole('img', { name: 'v003 captura (1440x900)' })).toHaveAttribute('src', 'blob:teste');
    expect(within(comparacaoNaTela).getByText(`${window.location.host}/design/7/checkout/v2`)).toBeInTheDocument();
  });

  it('o link copiado é o da página estável, e Apagar só aparece para quem pode', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());

    const comparacaoNaTela = await screen.findByRole('region', { name: 'Versões de Checkout' });
    expect(within(comparacaoNaTela).queryByRole('button', { name: 'Apagar a v001' })).not.toBeInTheDocument();
    await userEvent.click(within(comparacaoNaTela).getByRole('button', { name: 'Copiar link da v002' }));
    expect(entregues.copiado).toBe(`${window.location.origin}/design/7/checkout/v2`);
    expect(await within(comparacaoNaTela).findByText('Link copiado')).toBeInTheDocument();

    await userEvent.click(within(comparacaoNaTela).getByRole('button', { name: 'Apagar a v002' }));
    await waitFor(() => expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/design/7/checkout/v2', corpo: null }]));
    expect(await screen.findByText('v002 da tela Checkout apagada.')).toBeInTheDocument();
  });

  it('o filtro de branch deixa só as telas e as versões dele, e o modo Uma escolhe a versão', async () => {
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());

    await userEvent.selectOptions(await screen.findByLabelText('Branch'), 'feature/dev/semana3');
    const telas = within(screen.getByRole('region', { name: 'Telas' }));
    expect(telas.getAllByRole('button').map((b) => b.textContent)).toEqual([expect.stringMatching(/^Login/)]);

    await userEvent.selectOptions(screen.getByLabelText('Branch'), '');
    await userEvent.click(screen.getByRole('button', { name: 'Uma' }));
    await userEvent.selectOptions(screen.getByLabelText('Versão'), '1');
    expect(within(screen.getByRole('region', { name: 'Versões de Checkout' })).getAllByRole('article').map((a) => a.getAttribute('aria-label'))).toEqual(['v001 captura']);
  });

  it('o escopo Minhas traz o projeto do nível usuário, e sem nada no escopo aparece o vazio', async () => {
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());

    await userEvent.click(await screen.findByRole('button', { name: 'Minhas' }));
    expect(within(screen.getByRole('region', { name: 'Telas' })).getByRole('button', { name: /Rascunho/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Equipe' }));
    expect(screen.getByText(/Nenhuma tela neste escopo ainda/)).toBeInTheDocument();
  });

  it('quem não tem empresa usa /design, só com o nível usuário e sem os chips de escopo', async () => {
    servidor.use(http.get(`${API}/api/design/projetos`, () => HttpResponse.json(projetosDeDesign.filter((p) => p.nivel === 'usuario'))));
    renderizar('/design', tokenDoDev());

    expect(await screen.findByRole('heading', { name: 'Design' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /Rascunho/ })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Escopo' })).not.toBeInTheDocument();
  });

  it('a lista vazia diz de onde as telas vêm', async () => {
    servidor.use(http.get(`${API}/api/design/projetos`, () => HttpResponse.json([])));
    renderizar('/design', tokenDoDev());

    expect(await screen.findByText(/Nenhuma tela neste escopo ainda/)).toBeInTheDocument();
  });

  it('o link estável abre a página da versão, com a imagem e de onde ela saiu', async () => {
    renderizar('/design/7/checkout/v2', tokenDoGestor());

    expect(await screen.findByRole('heading', { name: 'Checkout · v002' })).toBeInTheDocument();
    expect(screen.getByText(/Proposta visual — nada implementado · 1440x900 · light/)).toBeInTheDocument();
    expect(screen.getByText(/semana5 @ 3f9c2a1/)).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: 'v002 proposta (1440x900)' })).toBeInTheDocument();
  });

  it('o link de uma versão que não existe mais diz isso', async () => {
    renderizar('/design/7/checkout/v9', tokenDoGestor());
    expect(await screen.findByText('Esta versão de design não existe mais, ou não é do seu escopo.')).toBeInTheDocument();
  });
});
