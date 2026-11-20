/** Somente endereços públicos entram no bundle. Um front isolado não fornece a API na mesma origem. */
export function validarConfiguracaoDePreview(env: Record<string, string | undefined>): void {
  for (const chave of ['VITE_API_URL', 'VITE_SITE_URL']) {
    const valor = env[chave] ?? '';
    let url: URL;
    try {
      url = new URL(valor);
    } catch {
      throw new Error(`Preview exige ${chave} com a URL HTTPS pública do destino de teste.`);
    }
    if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash
      || ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)) {
      throw new Error(`Preview exige ${chave} HTTPS pública, sem credencial, query ou fragmento.`);
    }
  }
}
