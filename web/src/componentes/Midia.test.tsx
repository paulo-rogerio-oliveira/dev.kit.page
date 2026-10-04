import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { Midia as DadosDaMidia } from '../conteudo/landing';
import { navegador, observadores } from '../testes/navegador';
import { Cta } from './Cta';
import { Midia } from './Midia';
import { Secao } from './Secao';

const MIDIA: DadosDaMidia = {
  video: { webm: '/midia/agente.webm', mp4: '/midia/agente.mp4' },
  poster: '/midia/agente.jpg',
  captura: '/capturas/tela-agente.png',
  alt: 'O agente trabalhando na task.',
  largura: 1280,
  altura: 720,
};

describe('Midia', () => {
  it('é um vídeo muted, em loop, playsinline, autoplay e preload=none, com poster e tamanho fixo', () => {
    const { container } = render(<Midia midia={MIDIA} />);

    const video = container.querySelector('video')!;
    expect(video).toHaveAttribute('poster', '/midia/agente.jpg');
    expect(video).toHaveAttribute('preload', 'none');
    expect(video).toHaveAttribute('loop');
    expect(video).toHaveAttribute('autoplay');
    expect(video).toHaveAttribute('playsinline');
    expect(video.muted).toBe(true);
    expect(video).toHaveAttribute('width', '1280');
    expect(video).toHaveAttribute('height', '720');
    expect(video).toHaveAccessibleName('O agente trabalhando na task.');
  });

  it('só carrega as fontes (WebM antes do MP4) quando chega à tela', () => {
    navegador.visivelNaHora = false;
    vi.mocked(HTMLMediaElement.prototype.load).mockClear();
    const { container } = render(<Midia midia={MIDIA} />);
    const video = container.querySelector('video')!;
    expect(container.querySelectorAll('source')).toHaveLength(0);
    expect(video.load).not.toHaveBeenCalled();

    act(() => observadores[0].entrar());

    const fontes = [...container.querySelectorAll('source')].map((s) => [s.getAttribute('src'), s.getAttribute('type')]);
    expect(fontes).toEqual([['/midia/agente.webm', 'video/webm'], ['/midia/agente.mp4', 'video/mp4']]);
    expect(video.load).toHaveBeenCalled();
  });

  it('com prefers-reduced-motion mostra só o poster, sem vídeo', () => {
    navegador.menosMovimento = true;
    const { container } = render(<Midia midia={MIDIA} />);

    expect(container.querySelector('video')).toBeNull();
    expect(screen.getByRole('img', { name: MIDIA.alt })).toHaveAttribute('src', '/midia/agente.jpg');
  });

  it('quando o vídeo falha, cai na captura estática com o mesmo texto alternativo', () => {
    const { container } = render(<Midia midia={MIDIA} />);

    fireEvent.error(container.querySelector('source[type="video/mp4"]')!);

    expect(container.querySelector('video')).toBeNull();
    expect(screen.getByRole('img', { name: MIDIA.alt })).toHaveAttribute('src', '/capturas/tela-agente.png');
  });

  it('sem vídeo, mostra a captura; sem captura, o poster', () => {
    const { rerender } = render(<Midia midia={{ ...MIDIA, video: undefined }} />);
    expect(screen.getByRole('img')).toHaveAttribute('src', '/capturas/tela-agente.png');

    rerender(<Midia midia={{ ...MIDIA, video: undefined, captura: undefined }} />);
    expect(screen.getByRole('img')).toHaveAttribute('src', '/midia/agente.jpg');
  });
});

describe('Secao e Cta', () => {
  it('a seção tem a âncora, o título ligado por aria-labelledby e o subtítulo', () => {
    render(<Secao id="beneficios" titulo="Benefícios" subtitulo="Por que usar">conteúdo</Secao>);

    const secao = screen.getByRole('region', { name: 'Benefícios' });
    expect(secao).toHaveAttribute('id', 'beneficios');
    expect(secao).toHaveTextContent('Por que usar');
  });

  it('o CTA com # é âncora da página e o resto é rota do app', () => {
    render(
      <MemoryRouter>
        <Cta para="#contato">Quero uma demonstração</Cta>
        <Cta para="/login" variante="secundario">Entrar</Cta>
      </MemoryRouter>,
    );

    expect(screen.getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
    expect(screen.getByRole('link', { name: 'Quero uma demonstração' })).toHaveClass('botao-primario');
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveClass('botao-fantasma');
  });
});
