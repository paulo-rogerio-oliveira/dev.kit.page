import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { Projeto } from '../api/projetos';

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
});
