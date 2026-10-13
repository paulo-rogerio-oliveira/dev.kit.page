import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { entregues } from '../testes/navegador';
import { Abas } from './Abas';
import { Avatar, iniciais } from './Avatar';
import { BarraDeUso } from './BarraDeUso';
import { BotaoCopiar } from './BotaoCopiar';
import { Chips } from './Chips';
import { Pagina } from './Pagina';
import { PainelLateral } from './PainelLateral';
import { Selo } from './Selo';

describe('Componentes compartilhados (US #461)', () => {
  it('Avatar: as iniciais da primeira e da última palavra, ou as duas letras da palavra só', () => {
    expect(iniciais('Ana Souza')).toBe('AS');
    expect(iniciais('Ana Maria de Souza')).toBe('AS');
    expect(iniciais('admin')).toBe('AD');
    expect(iniciais('ana.dev')).toBe('AD');
    expect(iniciais('Senior', 1)).toBe('S');
    expect(iniciais('  ')).toBe('?');

    const { container } = render(<Avatar nome="Teste" />);
    expect(container.firstChild).toHaveTextContent('TE');
    expect(container.firstChild).toHaveAttribute('aria-hidden', 'true');
  });

  it('Selo: a variante vira a classe, e o ponto é opcional', () => {
    render(<><Selo variante="sucesso" ponto>Ativo</Selo><Selo>Empresarial</Selo></>);

    expect(screen.getByText('Ativo')).toHaveClass('insignia', 'insignia-sucesso');
    expect(screen.getByText('Ativo').querySelector('.insignia-ponto')).not.toBeNull();
    expect(screen.getByText('Empresarial')).toHaveClass('insignia-neutro');
  });

  it('BarraDeUso: "N de M em uso" e a barra proporcional, que para cheia acima do total', () => {
    const { rerender } = render(<BarraDeUso usados={3} total={10} rotulo="Assentos" />);
    expect(screen.getByText('3 de 10 em uso')).toBeInTheDocument();
    const barra = screen.getByRole('progressbar', { name: 'Assentos' });
    expect(barra).toHaveAttribute('aria-valuenow', '3');
    expect(barra.firstChild).toHaveStyle({ width: '30%' });

    rerender(<BarraDeUso usados={12} total={10} rotulo="Assentos" />);
    expect(screen.getByText('12 de 10 em uso')).toBeInTheDocument();
    expect(screen.getByRole('progressbar').firstChild).toHaveStyle({ width: '100%' });
  });

  it('BotaoCopiar: copia o valor inteiro e diz que copiou', async () => {
    render(<BotaoCopiar texto="DK-7QH4-M2XA" rotulo="Copiar o código" />);

    await userEvent.click(screen.getByRole('button', { name: 'Copiar o código' }));

    expect(entregues.copiado).toBe('DK-7QH4-M2XA');
    expect(await screen.findByText('Copiado')).toBeInTheDocument();
  });

  it('BotaoCopiar: sem a área de transferência, diz que não copiou', async () => {
    const original = navigator.clipboard;
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: () => Promise.reject(new Error('negado')) } });
    render(<BotaoCopiar texto="x" rotulo="Copiar" />);

    await userEvent.click(screen.getByRole('button', { name: 'Copiar' }));

    expect(await screen.findByText('O navegador não deixou copiar')).toBeInTheDocument();
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: original });
  });

  it('Abas: a aba ativa é a selecionada, o clique e as setas mudam de aba', async () => {
    function Exemplo() {
      const [ativa, setAtiva] = useState('vitrine');
      return (
        <Abas rotulo="Seções" ativa={ativa} aoMudar={setAtiva} abas={[{ id: 'vitrine', rotulo: 'Vitrine' }, { id: 'releases', rotulo: 'Releases' }]}>
          <p>painel {ativa}</p>
        </Abas>
      );
    }
    render(<Exemplo />);

    expect(screen.getByRole('tab', { name: 'Vitrine' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toHaveTextContent('painel vitrine');

    await userEvent.click(screen.getByRole('tab', { name: 'Releases' }));
    expect(screen.getByRole('tab', { name: 'Releases' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: 'Releases' })).toHaveTextContent('painel releases');

    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Vitrine' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Vitrine' })).toHaveFocus();
  });

  it('Chips: a contagem ao lado do rótulo e o escolhido pressionado', async () => {
    const mudar = vi.fn();
    render(<Chips rotulo="Tipo" valor="todos" aoMudar={mudar} opcoes={[{ valor: 'todos', rotulo: 'Todos', contagem: 3 }, { valor: 'skill', rotulo: 'Skills', contagem: 2 }]} />);

    expect(screen.getByRole('button', { name: 'Todos 3' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Skills 2' }));
    expect(mudar).toHaveBeenCalledWith('skill');
  });

  it('Pagina e PainelLateral: o título, o subtítulo, a ação e o cartão lateral com o título dele', () => {
    render(
      <Pagina titulo="Empresas" subtitulo="1 empresa · 10 assentos contratados" acao={<span>ação</span>} emLinha>
        <PainelLateral id="t" titulo="Nova empresa"><p>formulário</p></PainelLateral>
      </Pagina>,
    );

    expect(screen.getByRole('heading', { level: 1, name: 'Empresas' })).toBeInTheDocument();
    expect(screen.getByText('1 empresa · 10 assentos contratados')).toBeInTheDocument();
    expect(screen.getByText('ação')).toBeInTheDocument();
    expect(screen.getByRole('complementary', { name: 'Nova empresa' })).toHaveTextContent('formulário');
  });
});
