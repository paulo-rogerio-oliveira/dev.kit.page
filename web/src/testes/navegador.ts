import { vi } from 'vitest';

// O que o jsdom não tem e a landing usa: matchMedia (prefers-reduced-motion), IntersectionObserver
// (o carregamento sob demanda da mídia) e o load/play do <video>. Os testes mudam o comportamento
// pelos controles abaixo; o setup.ts devolve o padrão depois de cada teste.

export const navegador = {
  /** O que o (prefers-reduced-motion: reduce) responde. */
  menosMovimento: false,
  /** Se o IntersectionObserver avisa na hora que o alvo está na tela. */
  visivelNaHora: true,
};

export function restaurarNavegador() {
  navegador.menosMovimento = false;
  navegador.visivelNaHora = true;
}

/** Os observadores criados, para o teste disparar a entrada na tela quando quiser. */
export const observadores: ObservadorDeMentira[] = [];

export class ObservadorDeMentira {
  readonly alvos: Element[] = [];

  constructor(private readonly avisar: IntersectionObserverCallback) {
    observadores.push(this);
  }

  observe(alvo: Element) {
    this.alvos.push(alvo);
    if (navegador.visivelNaHora) this.entrar();
  }

  /** Simula o alvo chegando à tela. */
  entrar() {
    const entradas = this.alvos.map((target) => ({ target, isIntersecting: true }) as unknown as IntersectionObserverEntry);
    this.avisar(entradas, this as unknown as IntersectionObserver);
  }

  unobserve() {}
  disconnect() {}
  takeRecords() {
    return [];
  }
}

export function instalarNavegador() {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (consulta: string) => ({
      matches: consulta.includes('prefers-reduced-motion') && navegador.menosMovimento,
      media: consulta,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }),
  });
  Object.defineProperty(window, 'IntersectionObserver', { configurable: true, value: ObservadorDeMentira });
  Object.defineProperty(globalThis, 'IntersectionObserver', { configurable: true, value: ObservadorDeMentira });
  HTMLMediaElement.prototype.load = vi.fn();
  HTMLMediaElement.prototype.play = vi.fn(() => Promise.resolve());
  HTMLMediaElement.prototype.pause = vi.fn();
}
