// Os tipos do roteiro.mjs, para o teste do conteúdo (src/conteudo/landing.test.ts) conferir que o roteiro grava
// só as mídias que a home usa e sem legenda sobreposta (US #467). O roteiro continua JavaScript: quem o roda é o
// Node, pelo `npm run midia`.

export interface Tela {
  arquivo: string;
  largura: number;
  altura: number;
}

/** Um passo da câmera: o destaque [x, y, largura, altura] em pixels da captura e o zoom. */
export interface Passo {
  em: number;
  destaque: [number, number, number, number];
  zoom: number;
  legenda?: string;
}

export interface Slide {
  tipo: 'tela' | 'terminal';
  duracao: number;
  tela?: Tela;
  passos?: Passo[];
  legenda?: string;
}

export interface MidiaDoRoteiro {
  nome: string;
  slides: Slide[];
}

export const TELAS: Record<string, Tela>;
export const MIDIAS: MidiaDoRoteiro[];
