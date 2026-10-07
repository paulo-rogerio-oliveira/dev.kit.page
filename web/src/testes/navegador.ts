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

/**
 * O que o dashboard entregou ao navegador (US #381): os arquivos baixados (o nome e o conteúdo) e o
 * texto copiado para a área de transferência.
 */
export const entregues = {
  arquivos: [] as { nome: string; conteudo: Blob }[],
  copiado: '',
};

export function restaurarNavegador() {
  navegador.menosMovimento = false;
  navegador.visivelNaHora = true;
  entregues.arquivos.length = 0;
  entregues.copiado = '';
}

/** O último Blob passado ao createObjectURL — o link de download o leva. */
let ultimoBlob: Blob | null = null;

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

  // O download (US #381): o jsdom não tem o createObjectURL nem navega para blob:, então o link com
  // `download` registra o arquivo em vez de navegar. Os outros links seguem o click de sempre.
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    value: (blob: Blob) => {
      ultimoBlob = blob;
      return 'blob:teste';
    },
  });
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: () => {} });
  const clicar = HTMLAnchorElement.prototype.click;
  HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) {
    if (this.download && ultimoBlob) entregues.arquivos.push({ nome: this.download, conteudo: ultimoBlob });
    else clicar.call(this);
  };
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: {
      writeText: (texto: string) => {
        entregues.copiado = texto;
        return Promise.resolve();
      },
    },
  });
}
