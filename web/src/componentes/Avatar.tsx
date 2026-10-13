/**
 * As iniciais de um nome: a primeira e a última palavra ("Ana Souza" → "AS"); uma palavra só dá as duas primeiras
 * letras ("admin" → "AD"). `letras = 1` é a inicial só (o avatar da empresa). O que não é letra nem número fica fora.
 */
export function iniciais(nome: string, letras: 1 | 2 = 2): string {
  const palavras = nome.trim().split(/[\s._@-]+/).map((p) => p.replace(/[^\p{L}\p{N}]/gu, '')).filter(Boolean);
  if (palavras.length === 0) return '?';
  if (letras === 1) return palavras[0][0].toUpperCase();
  const texto = palavras.length === 1 ? palavras[0].slice(0, 2) : palavras[0][0] + palavras[palavras.length - 1][0];
  return texto.toUpperCase();
}

/**
 * O avatar de iniciais (US #461): um quadrado arredondado com a cor derivada do nome — o mesmo nome tem sempre a
 * mesma cor. É decorativo: o nome ao lado é o que o leitor de tela lê.
 */
export function Avatar({ nome, letras = 2 }: { nome: string; letras?: 1 | 2 }) {
  let soma = 0;
  for (const c of nome) soma = (soma + c.charCodeAt(0)) % 997;
  return (
    <span className={`avatar avatar-${soma % 4}`} aria-hidden="true">{iniciais(nome, letras)}</span>
  );
}
