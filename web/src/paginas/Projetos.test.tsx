import { screen, waitFor, within } from '@testing-library/react';
import { vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { Projeto } from '../api/projetos';
import type { AcessosDoProjeto } from '../api/tipos';

const acessoInicial: AcessosDoProjeto = {
  recurso: 'projeto', projeto: 7, nome: 'Portal', papel: 'dono',
  dono: { id: 1, login: 'ana', nome: 'Ana' }, acessos: [],
  candidatos: [{ id: 2, login: 'bia', nome: 'Bia' }],
};

beforeEach(() => {
  servidor.use(http.get(`${API}/api/projetos/:id/acessos`, ({ params }) =>
    HttpResponse.json({ ...acessoInicial, projeto: Number(params.id) })));
});
afterEach(() => vi.restoreAllMocks());

const projeto: Projeto = {
  id: 7, slug: 'portal', nome: 'Portal', nivel: 'usuario', time: '', repositorios: ['https://github.com/acme/portal'],
  branch: 'feature/dev/projeto-portal', semanaConvertida: false, definicao: 'Plano', anexos: [], autonomia: true,
  paralelismo: 2, destinoDaPr: 'main', situacao: 'aprovado', versao: 1, autor: 'Dev', atualizadoEm: '2026-10-28T12:00:00Z',
  podeApagar: true, featureId: 700, epicId: 600, iteracao: 'DevKit/Sprint 3', board: 'vincular', itens: [
    { numero: 1, titulo: 'Contratos', descricao: '', fluxo: '6', repositorios: ['https://github.com/acme/portal'], componentes: [],
      dependencias: [], onda: 1, workItemId: 747, situacao: 'integrado', tipo: '', prioridade: 1, horas: 4, branch: 'feature/dev/projeto-portal-item-1' },
    { numero: 2, titulo: 'Cliente', descricao: '', fluxo: '6', repositorios: ['https://github.com/acme/portal'], componentes: [],
      dependencias: [1], onda: 2, workItemId: 748, situacao: 'rodando', tipo: '', prioridade: 1, horas: 8, branch: 'feature/dev/projeto-portal-item-2' },
  ],
};

describe('Projetos sincronizados', () => {
  it('leitor consulta snapshot portátil sem reenviar e preserva a referência ao navegar', async () => {
    const referencia = { projeto: 'repo-fixado', tela: 'plano', versao: 6, hash: 'a'.repeat(64) };
    const snapshot: Projeto = { ...projeto, board: 'nao', papel: 'leitor', baseDoBranch: 'develop',
      fluxoPadrao: '1', agente: 'codex', modelo: 'gpt-6.1-sol',
      modelos: [{ agente: 'codex', modelo: 'gpt-6.1-sol' }, { agente: 'claude', modelo: 'opus' }],
      itens: [{ ...projeto.itens[0], agente: 'codex', modelo: 'gpt-6.1-sol',
        design: { alteraTela: true, revisao: 2, propostas: [referencia] } }, projeto.itens[1]],
    };
    let envios = 0;
    servidor.use(
      http.get(`${API}/api/projetos`, () => HttpResponse.json([snapshot])),
      http.get(`${API}/api/projetos/7/acessos`, () => HttpResponse.json({ ...acessoInicial, papel: 'leitor', candidatos: [] })),
      http.get(`${API}/api/design/projetos`, () => HttpResponse.json([])),
      http.put(`${API}/api/projetos`, () => { envios++; return HttpResponse.json(snapshot); }),
      http.put(`${API}/api/projetos/7`, () => { envios++; return HttpResponse.json(snapshot); }),
    );
    renderizar('/projetos?slug=portal', tokenDoDev());
    const detalhe = within(await screen.findByRole('article', { name: 'Detalhe do projeto' }));
    expect(detalhe.getByText('1 Integrado')).toBeInTheDocument();
    expect(detalhe.getByText('Depende de: 1')).toBeInTheDocument();
    const acesso = within(await screen.findByRole('region', { name: 'Acesso' }));
    expect(await acesso.findByText(/Seu papel:/)).toHaveTextContent('Leitor');
    expect(acesso.queryByRole('button', { name: 'Dar acesso' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Propostas visuais' }));
    expect(await screen.findByText('repo-fixado/plano/v6')).toBeInTheDocument();
    expect(screen.getByText(`SHA-256: ${referencia.hash}`)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Plano$/ }));
    expect(await screen.findByText('Depende de: 1')).toBeInTheDocument();
    expect(envios).toBe(0);
  });

  it('transporta a referência para o detalhe sem selecionar outra versão quando o Design está ausente', async () => {
    const ref = { projeto: 'repo-congelado', tela: 'plano', versao: 6, hash: 'a'.repeat(64) };
    servidor.use(
      http.get(`${API}/api/projetos`, () => HttpResponse.json([{ ...projeto, itens: [
        { ...projeto.itens[0], design: { alteraTela: true, revisao: 2, propostas: [ref] } },
      ] }])),
      http.get(`${API}/api/design/projetos`, () => HttpResponse.json([])),
    );
    renderizar('/projetos?visao=propostas', tokenDoDev());
    expect(await screen.findByText('repo-congelado/plano/v6')).toBeInTheDocument();
    expect(screen.getByText(`SHA-256: ${ref.hash}`)).toBeInTheDocument();
    expect(screen.getByText('Revisão 2 · Altera tela')).toBeInTheDocument();
    expect(await screen.findByText(/Proposta fixada indisponível/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Ver proposta fixada/ })).not.toBeInTheDocument();
  });

  it('mostra dados, branches, ondas, andamento e conserva os filtros ao escolher outro projeto', async () => {
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto, { ...projeto, id: 8, nome: 'Outro' }])));
    renderizar('/projetos?nivel=usuario&situacao=aprovado', tokenDoDev());
    const detalhe = await screen.findByRole('article', { name: 'Detalhe do projeto' });
    expect(within(detalhe).getByText('feature/dev/projeto-portal')).toBeInTheDocument();
    expect(within(detalhe).getByText('feature/dev/projeto-portal-item-2')).toBeInTheDocument();
    expect(within(detalhe).getByText('Onda 2 de 2')).toBeInTheDocument();
    expect(within(detalhe).getByText('1 Integrado')).toBeInTheDocument();
    expect(within(detalhe).getByText('Depende de: 1')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Outro/ }));
    expect(within(detalhe).getByRole('heading', { name: 'Outro' })).toBeInTheDocument();
    expect(screen.getByLabelText('Nível de compartilhamento')).toHaveValue('usuario');
    expect(screen.getByLabelText('Situação do projeto')).toHaveValue('aprovado');
  });

  it('o Board remoto abre o projeto pelo slug e avisa quando ele ainda não chegou ao site', async () => {
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto, { ...projeto, id: 8, slug: 'outro', nome: 'Outro' }])));
    renderizar('/projetos?slug=outro', tokenDoDev());
    const detalhe = await screen.findByRole('article', { name: 'Detalhe do projeto' });
    expect(within(detalhe).getByRole('heading', { name: 'Outro' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Outro/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('o slug que o site ainda não tem cai no primeiro projeto com o aviso de sincronizar', async () => {
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto])));
    renderizar('/projetos?slug=criado-agora', tokenDoDev());
    expect(await screen.findByText(/O projeto criado-agora ainda não chegou ao site/)).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Detalhe do projeto' })).getByRole('heading', { name: 'Portal' })).toBeInTheDocument();
  });

  it('mostra estado vazio e permite repetir uma consulta que falhou', async () => {
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json({ detail: 'Sem conexão' }, { status: 503 })));
    renderizar('/projetos', tokenDoDev());
    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível carregar os projetos');
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([])));
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByText(/Nenhum projeto sincronizado/)).toBeInTheDocument();
  });

  it('exige login para consultar os projetos', async () => {
    renderizar('/projetos');
    expect(await screen.findByRole('heading', { name: /Entrar/i })).toBeInTheDocument();
  });

  it('o dono concede, altera o papel e remove com confirmação pelo painel Acesso', async () => {
    let acesso = structuredClone(acessoInicial);
    const concedidos: unknown[] = [];
    const alterados: unknown[] = [];
    let removidos = 0;
    servidor.use(
      http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto])),
      http.get(`${API}/api/projetos/7/acessos`, () => HttpResponse.json(acesso)),
      http.post(`${API}/api/projetos/7/acessos`, async ({ request }) => {
        concedidos.push(await request.json());
        acesso = { ...acesso, candidatos: [], acessos: [{ usuario: acessoInicial.candidatos[0], papel: 'leitor', concedidoEm: '2026-11-05T12:00:00Z' }] };
        return HttpResponse.json(acesso);
      }),
      http.put(`${API}/api/projetos/7/acessos/2`, async ({ request }) => {
        alterados.push(await request.json());
        acesso = { ...acesso, acessos: [{ ...acesso.acessos[0], papel: 'editor' }] };
        return HttpResponse.json(acesso);
      }),
      http.delete(`${API}/api/projetos/7/acessos/2`, () => {
        removidos++;
        acesso = structuredClone(acessoInicial);
        return HttpResponse.json(acesso);
      }),
    );
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    renderizar('/projetos', tokenDoDev());
    const painel = within(await screen.findByRole('region', { name: 'Acesso' }));
    expect(await painel.findByText(/Seu papel:/)).toHaveTextContent('Dono');
    expect(painel.getByText('Ana (ana)')).toBeInTheDocument();
    await userEvent.selectOptions(painel.getByLabelText('Usuário da empresa'), 'bia');
    await userEvent.click(painel.getByRole('button', { name: 'Dar acesso' }));
    await waitFor(() => expect(concedidos).toEqual([{ usuario: 'bia', papel: 'leitor' }]));
    await userEvent.selectOptions(await painel.findByLabelText('Papel de bia'), 'editor');
    await waitFor(() => expect(alterados).toEqual([{ papel: 'editor' }]));
    await userEvent.click(painel.getByRole('button', { name: 'Remover bia' }));
    expect(removidos).toBe(0);
    expect(confirmar).toHaveBeenLastCalledWith(expect.stringContaining('ao projeto Portal?'));
    await userEvent.click(painel.getByRole('button', { name: 'Remover bia' }));
    await waitFor(() => expect(removidos).toBe(1));
    expect(await painel.findByText('Ninguém além do dono tem acesso a este projeto.')).toBeInTheDocument();
  });

  it.each(['leitor', 'editor'] as const)('%s vê dono e papel, sem controles de gestão', async papel => {
    servidor.use(
      http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto])),
      http.get(`${API}/api/projetos/7/acessos`, () => HttpResponse.json({ ...acessoInicial, papel, candidatos: [] })),
    );
    renderizar('/projetos', tokenDoDev());
    const painel = within(await screen.findByRole('region', { name: 'Acesso' }));
    expect(await painel.findByText(/Seu papel:/)).toHaveTextContent(papel === 'leitor'
      ? 'Leitor — vê o projeto, os itens e o andamento' : 'Editor — vê o projeto, altera, aprova e dispara');
    expect(painel.queryByRole('form', { name: 'Dar acesso' })).not.toBeInTheDocument();
    expect(painel.queryByRole('button', { name: /Remover/ })).not.toBeInTheDocument();
  });

  it('a recusa da API aparece sem esconder o projeto', async () => {
    servidor.use(
      http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto])),
      http.post(`${API}/api/projetos/7/acessos`, () => HttpResponse.json({ detail: 'Só o dono concede acesso.' }, { status: 403 })),
    );
    renderizar('/projetos', tokenDoDev());
    const painel = within(await screen.findByRole('region', { name: 'Acesso' }));
    await userEvent.selectOptions(await painel.findByLabelText('Usuário da empresa'), 'bia');
    await userEvent.click(painel.getByRole('button', { name: 'Dar acesso' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Só o dono concede acesso.');
    expect(screen.getByRole('article', { name: 'Detalhe do projeto' })).toBeInTheDocument();
  });
});
