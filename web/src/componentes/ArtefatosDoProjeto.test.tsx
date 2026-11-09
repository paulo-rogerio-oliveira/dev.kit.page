import { render, screen } from '@testing-library/react';
import { ArtefatosDoCard, tamanhoDoArtefato } from './ArtefatosDoProjeto';
import type { Artefato } from '../api/projetos';

describe('tamanho do pacote no card de artefatos', () => {
  // (controle-v4, item 5) A evidência ponta a ponta achou o pacote de 445 bytes como "0 MB": abaixo de 1 MB sai em KB ou bytes.
  it.each([
    [445, '445 bytes'],
    [1023, '1023 bytes'],
    [1024, '1 KB'],
    [12_800, '12,5 KB'],
    [1_048_524, '1.023,9 KB'],
    [1048575, '1,0 MB'],
    [1048576, '1,0 MB'],
    [43_200_000, '41,2 MB'],
  ])('%i bytes → %s', (bytes, esperado) => {
    expect(tamanhoDoArtefato(bytes)).toBe(esperado);
  });

  it('o link Baixar do pacote pequeno mostra o tamanho em bytes, e não "0 MB"', () => {
    const pacote: Artefato = {
      id: 1, projetoId: 11, tipo: 'pacote', repositorio: 'https://github.com/empresa-demo/git.kit', sha: '2412df94c1', link: 'https://exemplo.test/zip',
      expiraEm: new Date(Date.now() + 23.5 * 3600_000).toISOString(), hash: 'ab'.repeat(32), tamanho: 445, ambiente: '', autor: 'Ana Demo', criadoEm: new Date().toISOString(),
    };
    render(<ArtefatosDoCard nome="controle-v4" slug="controle-v4" artefatos={[pacote]} gerar={() => undefined} desabilitado={false} motivo="" agora={Date.now()} />);
    const baixar = screen.getByRole('link', { name: 'Baixar o pacote de git.kit · 2412df9' });
    expect(baixar).toHaveTextContent('445 bytes · expira em 23 h');
    expect(baixar).not.toHaveTextContent('0 MB');
  });
});
