import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { validarConfiguracaoDePreview } from '../../configuracaoDePreview';

const destino = { VITE_API_URL: 'https://api-teste.exemplo.com', VITE_SITE_URL: 'https://app-preview.azurestaticapps.net' };

describe('configuração pública do preview', () => {
  it('aceita API externa e site de teste explícitos', () => {
    expect(() => validarConfiguracaoDePreview(destino)).not.toThrow();
  });

  it.each(['VITE_API_URL', 'VITE_SITE_URL'])('recusa %s ausente ou com segredo no endereço', (chave) => {
    for (const valor of ['', '/api', 'http://localhost:5080', 'https://user:pass@exemplo.com', 'https://exemplo.com?token=valor']) {
      expect(() => validarConfiguracaoDePreview({ ...destino, [chave]: valor })).toThrow(chave);
    }
  });

  it('preserva deep-link sem devolver index.html para API ou assets inexistentes', () => {
    const config = JSON.parse(readFileSync(resolve(process.cwd(), 'public/staticwebapp.config.json'), 'utf8'));
    expect(config.navigationFallback.rewrite).toBe('/index.html');
    expect(config.navigationFallback.exclude).toEqual(expect.arrayContaining(['/api/*', '/assets/*']));
  });
});
