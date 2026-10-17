import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { api } from '../api/cliente';
import type { ItemDeContexto } from '../api/tipos';
import { contextoDaEmpresa, contextoDeVendas, escritas, instrucaoDaAna, sugestaoDoAgente } from '../testes/servidor';
import { EditorDeContexto, TETO_DO_VALOR } from './EditorDeContexto';

afterEach(() => vi.restoreAllMocks());

/** O editor sozinho, ligado às rotas do token no servidor de mentira (a Ana: grava o dela e o da equipe vendas). */
function abrir(itens: ItemDeContexto[] = [contextoDaEmpresa, contextoDeVendas, instrucaoDaAna, sugestaoDoAgente]) {
  const aoMudar = vi.fn(() => Promise.resolve());
  const aoExpirar = vi.fn();
  render(
    <EditorDeContexto
      itens={itens}
      niveis={['usuario', 'equipe']}
      equipes={['vendas']}
      podeEditar={(i) => i.nivel !== 'empresa'}
      aoGravar={(g) => api.gravarContexto('token-dev', g)}
      aoExcluir={(i) => api.excluirContexto('token-dev', i.id)}
      aoPromover={(i) => api.promoverContexto('token-dev', i.id)}
      aoMudar={aoMudar}
      aoExpirar={aoExpirar}
    />,
  );
  return { aoMudar, aoExpirar };
}

const linhaDe = (texto: string) => screen.getByText(texto).closest('tr')!;

describe('Editor do contexto (US #556)', () => {
  it('mostra o nível, a origem com o selo Instrução × Sugestão, e filtra', async () => {
    abrir();

    expect(linhaDe('Prefiro testes pequenos')).toHaveTextContent(/Usuário.*Ana Souza.*contexto\.testes.*Instrução/);
    expect(linhaDe('Rode só a suíte do projeto alterado')).toHaveTextContent(/contexto\.testes.*Sugestão/);
    expect(linhaDe('vendas/{id}')).toHaveTextContent(/Equipe.*vendas.*branch\.feature.*Instrução/);

    await userEvent.click(screen.getByRole('button', { name: /^Sugestões/ }));
    expect(within(screen.getByRole('table')).getAllByRole('row').slice(1)).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: /^Equipe/ }));
    expect(screen.getByText(/Nenhum item aqui/)).toBeInTheDocument();
  });

  it('a sugestão do agente se promove ou se descarta; a instrução se edita ou se exclui; a da empresa é só leitura', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const { aoMudar } = abrir();

    const sugestao = linhaDe('Rode só a suíte do projeto alterado');
    expect(within(sugestao).queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
    await userEvent.click(within(sugestao).getByRole('button', { name: 'Promover contexto.testes (Usuário)' }));
    expect(await screen.findByText('Sugestão contexto.testes promovida a instrução.')).toBeInTheDocument();

    await userEvent.click(within(sugestao).getByRole('button', { name: 'Descartar contexto.testes (Usuário)' }));
    expect(await screen.findByText('Sugestão contexto.testes descartada.')).toBeInTheDocument();
    expect(window.confirm).toHaveBeenCalledWith('Descartar a sugestão contexto.testes?');

    // Excluir pede confirmação, e a página relê (o item some da lista que ela devolve).
    await userEvent.click(within(linhaDe('Prefiro testes pequenos')).getByRole('button', { name: 'Excluir contexto.testes (Usuário)' }));
    expect(await screen.findByText('contexto.testes excluído.')).toBeInTheDocument();
    expect(window.confirm).toHaveBeenLastCalledWith('Excluir contexto.testes do nível usuário?');

    expect(within(linhaDe('.NET 10 e React 19')).queryByRole('button')).not.toBeInTheDocument();
    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/contexto/4/promover', corpo: null },
      { metodo: 'DELETE', caminho: '/api/contexto/4', corpo: null },
      { metodo: 'DELETE', caminho: '/api/contexto/3', corpo: null },
    ]));
    expect(aoMudar).toHaveBeenCalledTimes(3);
  });

  it('não exclui sem a confirmação', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    abrir();
    await userEvent.click(within(linhaDe('Prefiro testes pequenos')).getByRole('button', { name: 'Excluir contexto.testes (Usuário)' }));
    expect(escritas).toEqual([]);
  });

  it('grava a instrução da equipe com o contador do teto', { timeout: 15_000 }, async () => {
    abrir();
    const formulario = screen.getByRole('form', { name: 'Gravar no contexto' });
    expect(within(within(formulario).getByLabelText('Nível')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Usuário (o seu)', 'Equipe']);

    await userEvent.selectOptions(within(formulario).getByLabelText('Nível'), 'equipe');
    expect(within(formulario).getByLabelText('Equipe')).toHaveValue('vendas');
    await userEvent.clear(within(formulario).getByLabelText('Chave'));
    await userEvent.type(within(formulario).getByLabelText('Chave'), 'contexto.revisao');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Dois revisores');
    expect(screen.getByText(`14/${TETO_DO_VALOR} caracteres`)).toBeInTheDocument();
    expect(within(formulario).getByLabelText('Texto')).toHaveAttribute('maxLength', String(TETO_DO_VALOR));
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));

    expect(await screen.findByText('contexto.revisao gravado no nível equipe.')).toBeInTheDocument();
    expect(escritas.map((e) => e.corpo)).toEqual([{ nivel: 'equipe', chave: 'contexto.revisao', valor: 'Dois revisores', equipe: 'vendas' }]);
  });

  it('o conflito (409) aparece com o valor atual para reaplicar — nada é sobrescrito em silêncio', { timeout: 15_000 }, async () => {
    // A lista da tela é a de antes: a versão 1 do item da equipe, que no servidor já é a 2.
    abrir([{ ...contextoDeVendas, versao: 1 }]);

    await userEvent.click(screen.getByRole('button', { name: 'Editar branch.feature (Equipe)' }));
    const texto = screen.getByLabelText('Texto');
    await userEvent.clear(texto);
    await userEvent.type(texto, 'vendas/meu/{{id}');
    await userEvent.click(screen.getByRole('button', { name: 'Gravar' }));

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent(/alterado por outra pessoa/);
    expect(alerta).toHaveTextContent(/Valor atual \(versão 2, de Bia\): vendas\/v2\/\{id\}/);

    // Reaplicar manda a MESMA edição sobre a versão atual.
    await userEvent.click(within(alerta).getByRole('button', { name: 'Reaplicar a minha edição' }));
    await waitFor(() => expect(escritas.map((e) => (e.corpo as { versaoEsperada?: number }).versaoEsperada)).toEqual([1, 2]));
    expect(await screen.findByText('branch.feature gravado no nível equipe.')).toBeInTheDocument();
  });

  it('a recusa da credencial (422) aparece sem ecoar o valor', { timeout: 15_000 }, async () => {
    abrir();
    await userEvent.type(screen.getByLabelText('Texto'), 'use ghp_1234567890abcdefghij');
    await userEvent.click(screen.getByRole('button', { name: 'Gravar' }));

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent(/parece conter uma credencial/);
    expect(alerta).not.toHaveTextContent(/ghp_/);
  });
});
