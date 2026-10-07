import type { ArquivoBaixado } from './api/tipos';

/**
 * Entrega ao navegador um arquivo baixado da API (as exportações da US #381): um link temporário com
 * `download`, clicado e descartado. O conteúdo já veio autenticado pelo cliente da API — um link
 * direto para a rota não levaria o token.
 */
export function salvarArquivo(arquivo: ArquivoBaixado) {
  const url = URL.createObjectURL(arquivo.conteudo);
  const link = document.createElement('a');
  link.href = url;
  link.download = arquivo.nome;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
