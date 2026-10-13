/**
 * Copia o texto para a área de transferência. Devolve se deu certo: o navegador recusa fora de contexto seguro
 * (http sem localhost) ou sem permissão, e quem chama diz o que fazer em vez de fingir que copiou.
 */
export async function copiarTexto(texto: string): Promise<boolean> {
  try {
    if (!navigator.clipboard) return false;
    await navigator.clipboard.writeText(texto);
    return true;
  } catch {
    return false;
  }
}
