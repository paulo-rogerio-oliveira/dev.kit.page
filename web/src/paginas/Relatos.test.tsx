import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, CAPTURA_PNG, escritas, requisicoes, servidor, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';
import { enderecoDaCaptura } from './Relatos';

const TEXTO_DO_CASO_REAL = 'A PR da task multirepo foi criada no repositório errado (dev.kit.page em vez de git.kit)';

/** A última consulta da lista de relatos que chegou ao servidor. */
const ultimaConsulta = () => [...requisicoes].reverse().find((u) => u.pathname === '/api/relatos')!;

/** O botão que abre o relato (o começo do texto, na linha dele). */
const botaoDoRelato = (id: number) => screen.findByRole('button', { name: new RegExp(`^Abrir o relato ${id}:`) });

/** A linha da lista de um relato (pelo número). */
async function linhaDoRelato(id: number) {
  return (await botaoDoRelato(id)).closest('tr')!;
}

async function abrir(id: number) {
  await userEvent.click(await botaoDoRelato(id));
  return screen.findByRole('article', { name: 'Detalhe do relato' });
}

// As consultas por papel no jsdom, com a lista e o detalhe na tela, passam do padrão de 5 s quando a suíte inteira roda junto.
describe('Relatos (US #602)', { timeout: 30_000 }, () => {
  it('lista os relatos com a categoria, o estado, o autor, a empresa e os indicadores de captura e de resposta', async () => {
    renderizar('/relatos', tokenValido());

    const defeito = await linhaDoRelato(41);
    // A paleta da marca (versão 5): o defeito no vermelho da marca.
    expect(within(defeito).getByText('Defeito')).toHaveClass('insignia-marca');
    expect(within(defeito).getByText('Novo')).toBeInTheDocument();
    expect(within(defeito).getByText('Ana Souza')).toBeInTheDocument();
    expect(within(defeito).getByText('Empresa A')).toBeInTheDocument();
    expect(within(defeito).getByText('Com captura')).toBeInTheDocument();
    expect(within(defeito).queryByText('Respondido')).not.toBeInTheDocument();

    const sugestao = await linhaDoRelato(42);
    expect(within(sugestao).getByText('Sugestão')).toBeInTheDocument();
    expect(within(sugestao).getByText('Em análise')).toBeInTheDocument();
    expect(within(sugestao).getByText('Respondido')).toBeInTheDocument();
    expect(screen.getByText('Página 1 de 1 · 2 relato(s)')).toBeInTheDocument();
    expect(ultimaConsulta().search).toBe('?pagina=1&tamanho=20');
  });

  it('os filtros montam a query: estado, categoria, empresa (do admin) e o período', async () => {
    renderizar('/relatos', tokenValido());
    await linhaDoRelato(41);
    await screen.findByRole('option', { name: 'Empresa B' });

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por estado'), 'em-analise');
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por categoria'), 'sugestao');
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por empresa'), '5');
    fireEvent.change(screen.getByLabelText('De'), { target: { value: '2026-10-01' } });
    fireEvent.change(screen.getByLabelText('Até'), { target: { value: '2026-10-18' } });

    await waitFor(() => expect(Object.fromEntries(ultimaConsulta().searchParams)).toEqual({
      estado: 'em-analise', categoria: 'sugestao', empresa: '5', de: '2026-10-01', ate: '2026-10-18', pagina: '1', tamanho: '20',
    }));
    expect(await screen.findByRole('button', { name: /^Abrir o relato 42:/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Abrir o relato 41:/ })).not.toBeInTheDocument();

    // Um filtro sem resultado diz isso; Limpar filtros volta à consulta sem filtro.
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por categoria'), 'duvida');
    expect(await screen.findByText('Nenhum relato com esses filtros.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }));
    await waitFor(() => expect(ultimaConsulta().search).toBe('?pagina=1&tamanho=20'));
  });

  it('o gestor não vê o filtro de empresa; o admin vê', async () => {
    const { unmount } = renderizar('/relatos', tokenDoGestor());
    await linhaDoRelato(41);
    expect(screen.getByLabelText('Filtrar por estado')).toBeInTheDocument();
    expect(screen.queryByLabelText('Filtrar por empresa')).not.toBeInTheDocument();
    expect(screen.getByText('Defeitos, sugestões e dúvidas dos usuários da Empresa A.')).toBeInTheDocument();
    unmount();
    sessionStorage.clear();

    renderizar('/relatos', tokenValido());
    expect(await screen.findByLabelText('Filtrar por empresa')).toBeInTheDocument();
  });

  it('o detalhe mostra o texto, o contexto em <pre> e a captura', async () => {
    renderizar('/relatos', tokenValido());

    const detalhe = await abrir(41);

    expect(await within(detalhe).findByText(TEXTO_DO_CASO_REAL)).toBeInTheDocument();
    const contexto = within(detalhe).getByLabelText('Contexto do relato');
    expect(contexto.tagName).toBe('PRE');
    expect(contexto).toHaveTextContent('"task":"t_602"');
    const captura = within(detalhe).getByRole('img', { name: 'Captura de tela enviada com o relato 41' });
    expect(captura).toHaveAttribute('src', `data:image/png;base64,${CAPTURA_PNG}`);
    expect(within(detalhe).getByText('Ainda sem resposta. Responder um relato novo o leva a em análise.')).toBeInTheDocument();
  });

  it('trocar o estado chama a API e atualiza o detalhe e a lista', async () => {
    renderizar('/relatos', tokenValido());
    const detalhe = await abrir(41);
    const estados = await within(detalhe).findByRole('group', { name: 'Estado do relato' });
    expect(within(estados).getByRole('button', { name: 'Novo' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(estados).getByRole('button', { name: 'Em análise' }));
    expect(await screen.findByText('Relato 41 marcado como em análise.')).toBeInTheDocument();
    expect(within(estados).getByRole('button', { name: 'Em análise' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(estados).getByRole('button', { name: 'Resolvido' }));
    expect(await screen.findByText('Relato 41 marcado como resolvido.')).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByRole('button', { name: /^Abrir o relato 41:/ }).closest('tr')!).getByText('Resolvido')).toBeInTheDocument());
    expect(escritas).toEqual([
      { metodo: 'PATCH', caminho: '/api/relatos/41/estado', corpo: { estado: 'em-analise' } },
      { metodo: 'PATCH', caminho: '/api/relatos/41/estado', corpo: { estado: 'resolvido' } },
    ]);
  });

  it('responder chama a API, mostra quem respondeu e marca o relato como respondido na lista', async () => {
    renderizar('/relatos', tokenValido());
    const detalhe = await abrir(41);

    await userEvent.type(await within(detalhe).findByLabelText('Resposta'), 'Corrigido na versão 138: a PR vai para o repositório da task.');
    await userEvent.click(within(detalhe).getByRole('button', { name: 'Responder' }));

    expect(await screen.findByText(/Resposta gravada no relato 41/)).toBeInTheDocument();
    expect(within(detalhe).getByTestId('resposta-atual')).toHaveTextContent('Respondido por admin');
    expect(within(detalhe).getByTestId('resposta-atual')).toHaveTextContent('Corrigido na versão 138');
    // Responder um relato novo o leva a em análise.
    expect(within(within(detalhe).getByRole('group', { name: 'Estado do relato' })).getByRole('button', { name: 'Em análise' })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => expect(within(screen.getByRole('button', { name: /^Abrir o relato 41:/ }).closest('tr')!).getByText('Respondido')).toBeInTheDocument());
    expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/relatos/41/resposta', corpo: { texto: 'Corrigido na versão 138: a PR vai para o repositório da task.' } },
    ]);
  });

  it('a resposta já dada aparece com quem respondeu', async () => {
    renderizar('/relatos', tokenValido());
    const detalhe = await abrir(42);

    expect(await within(detalhe).findByTestId('resposta-atual')).toHaveTextContent('Respondido por admin');
    expect(within(detalhe).getByLabelText('Resposta')).toHaveValue('Entrou no planejamento.');
    expect(within(detalhe).getByRole('button', { name: 'Atualizar a resposta' })).toBeDisabled();
    expect(within(detalhe).getByText('O usuário não mandou o contexto.')).toBeInTheDocument();
    expect(within(detalhe).queryByRole('img')).not.toBeInTheDocument();
  });

  it('o 403 da API mostra "Sem permissão para ver os relatos"', async () => {
    servidor.use(http.get(`${API}/api/relatos`, () => new HttpResponse(null, { status: 403 })));
    renderizar('/relatos', tokenDoGestor());

    expect(await screen.findByRole('alert')).toHaveTextContent('Sem permissão para ver os relatos.');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('o dev que abre /relatos vai à conta dele', async () => {
    renderizar('/relatos', tokenDoDev());
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Sua conta');
  });

  it('o admin chega aos relatos pelo menu, e o gestor pelo dashboard', async () => {
    const { unmount } = renderizar('/usuarios', tokenValido());
    await userEvent.click(await screen.findByRole('link', { name: 'Relatos' }));
    expect(await screen.findByRole('heading', { name: 'Relatos', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Relatos' })).toHaveClass('link-ativo');
    unmount();
    sessionStorage.clear();

    renderizar('/dashboard', tokenDoGestor());
    await userEvent.click(await screen.findByRole('link', { name: 'Relatos' }));
    expect(await screen.findByRole('heading', { name: 'Relatos', level: 1 })).toBeInTheDocument();
  });

  it('a captura em JPEG vira data:image/jpeg', () => {
    expect(enderecoDaCaptura('/9j/4AAQ')).toBe('data:image/jpeg;base64,/9j/4AAQ');
    expect(enderecoDaCaptura(CAPTURA_PNG)).toBe(`data:image/png;base64,${CAPTURA_PNG}`);
  });
});
