import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, qualidadeVazia, requisicoes, servidor, tokenValido } from '../testes/servidor';

const kpi = (rotulo: string) => screen.getByTestId(`kpi-${rotulo}`);

describe('Dashboard', () => {
  it('mostra os KPIs de quantidade e qualidade com os valores da API', async () => {
    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    expect(kpi('Sessões')).toHaveTextContent('3');
    expect(kpi('Turnos')).toHaveTextContent('8');
    expect(kpi('Ferramentas acionadas')).toHaveTextContent('41');
    expect(kpi('Arquivos alterados')).toHaveTextContent('17');
    expect(kpi('Taxa de falha de turno')).toHaveTextContent(/25,0\s?%/);
    expect(kpi('Duração média do turno')).toHaveTextContent('42,5 s');
    expect(kpi('Nota média dos avaliadores')).toHaveTextContent('91,5');
    expect(kpi('Objetivos cumpridos / recusados')).toHaveTextContent('1 / 1');
    expect(screen.getByText('limite-de-uso')).toBeInTheDocument();
  });

  it('mostra os eventos recentes', async () => {
    renderizar('/dashboard', tokenValido());

    const tabela = (await screen.findByText('TurnoExecutado')).closest('table')!;
    expect(within(tabela).getByText('claude')).toBeInTheDocument();
    expect(within(tabela).getByText('máquina a1b2c3d4')).toBeInTheDocument();
    expect(screen.getByText(/Página 1 de 1 · 1 eventos/)).toBeInTheDocument();
  });

  it('divisor zero aparece como traço, nunca NaN', async () => {
    servidor.use(http.get(`${API}/api/dashboard/qualidade`, () => HttpResponse.json(qualidadeVazia)));

    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Qualidade de uso' });
    expect(kpi('Taxa de falha de turno')).toHaveTextContent('—');
    expect(kpi('Nota média dos avaliadores')).toHaveTextContent('—');
    expect(kpi('Turnos por objetivo cumprido')).toHaveTextContent('—');
    expect(document.body).not.toHaveTextContent('NaN');
    expect(screen.getByText('Nenhuma falha de turno no período.')).toBeInTheDocument();
  });

  it('os filtros de máquina e de período refazem a consulta', async () => {
    renderizar('/dashboard', tokenValido());
    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    await screen.findByRole('option', { name: /máquina a1b2c3d4/ });

    await userEvent.selectOptions(screen.getByLabelText('Máquina'), '2');
    await waitFor(() => expect(requisicoes.at(-1)?.searchParams.get('maquina')).toBe('2'));

    await userEvent.selectOptions(screen.getByLabelText('Período'), '7');
    await waitFor(() => {
      const ultima = requisicoes.at(-1)!;
      const dias = (Date.parse(ultima.searchParams.get('ate')!) - Date.parse(ultima.searchParams.get('de')!)) / 86_400_000 + 1;
      expect(dias).toBe(7);
    });
  });

  it('sem máquinas, explica como ligar o envio', async () => {
    servidor.use(http.get(`${API}/api/dashboard/maquinas`, () => HttpResponse.json([])));

    renderizar('/dashboard', tokenValido());

    expect(await screen.findByText(/Nenhuma máquina enviou telemetria ainda/)).toBeInTheDocument();
  });
});
