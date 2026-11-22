import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArtefatosDoCard } from './ArtefatosDoProjeto';
import { CHAVE_DA_SESSAO, SessaoProvider, useSessao } from '../sessao';
import { tokenDoDev } from '../testes/servidor';

const repos = ['github.com/demo/desktop', 'github.com/demo/web'];
const sha = 'abcdef0123456789abcdef0123456789abcdef01';

function Percurso({ gerar }: { gerar: () => void }) {
  const [plano, setPlano] = useState(false);
  const [maquina, setMaquina] = useState('host-a');
  const [slug, setSlug] = useState('projeto-a');
  const [leitor, setLeitor] = useState(false);
  const [origemConfirmada, setOrigemConfirmada] = useState(true);
  const { entrar, sair } = useSessao();
  return <>
    <button onClick={() => { setPlano(p => !p); setOrigemConfirmada(false); }}>{plano ? 'Voltar ao Board' : 'Abrir plano'}</button>
    <button onClick={() => setMaquina(m => m === 'host-a' ? 'host-b' : 'host-a')}>Trocar host</button>
    <button onClick={() => setSlug(s => s === 'projeto-a' ? 'projeto-b' : 'projeto-a')}>Trocar projeto</button>
    <button onClick={() => entrar({ ...tokenDoDev(), token: 'sessao-distinta' })}>Trocar sessão</button>
    <button onClick={() => sair('expirou')}>Encerrar sessão</button>
    <button onClick={() => setLeitor(true)}>Revogar edição</button>
    {!plano && <ArtefatosDoCard maquina={maquina} nome="Projeto" slug={slug} repositorios={repos}
      artefatos={[]} gerar={gerar} desabilitado={leitor} motivo="Leitor" agora={Date.now()} capacidade
      plano={origemConfirmada ? { tipo: 'origem-artefato', projeto: slug, repositorio: repos[1], branch: 'feature/demo', sha, formato: 'preview' } : null} />}
  </>;
}

function abrir(gerar = vi.fn()) {
  sessionStorage.setItem(CHAVE_DA_SESSAO, JSON.stringify(tokenDoDev()));
  render(<SessaoProvider><Percurso gerar={gerar} /></SessaoProvider>);
  return gerar;
}

async function editar() {
  await userEvent.click(screen.getByRole('button', { name: 'Gerar artefato', exact: true }));
  await userEvent.selectOptions(screen.getByLabelText('Repositório', { exact: true }), repos[1]);
  await userEvent.selectOptions(screen.getByLabelText('Artefato', { exact: true }), 'preview');
  await userEvent.type(screen.getByLabelText('Commit (opcional)'), sha);
}

describe('rascunho de artefato entre rotas', () => {
  it('conserva escolhas após desmontar o painel e isola host/projeto/sessão, sem gerar', async () => {
    const gerar = abrir();
    await editar();
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar', exact: true }));
    await userEvent.click(screen.getByRole('button', { name: 'Abrir plano' }));
    await userEvent.click(screen.getByRole('button', { name: 'Voltar ao Board' }));
    await userEvent.click(screen.getByRole('button', { name: 'Gerar artefato', exact: true }));
    expect(screen.getByLabelText('Repositório', { exact: true })).toHaveValue(repos[1]);
    expect(screen.getByLabelText('Artefato', { exact: true })).toHaveValue('preview');
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue(sha);
    for (const nome of ['Trocar host', 'Trocar projeto']) {
      await userEvent.click(screen.getByRole('button', { name: nome }));
      expect(screen.getByLabelText('Artefato', { exact: true })).toHaveValue('');
      expect(screen.getByLabelText('Commit (opcional)')).toHaveValue('');
      await userEvent.click(screen.getByRole('button', { name: nome }));
      expect(screen.getByLabelText('Commit (opcional)')).toHaveValue(sha);
    }
    await userEvent.click(screen.getByRole('button', { name: 'Trocar sessão' }));
    expect(screen.getByLabelText('Repositório', { exact: true })).toHaveValue(repos[0]);
    expect(screen.getByLabelText('Artefato', { exact: true })).toHaveValue('');
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue('');
    await userEvent.type(screen.getByLabelText('Commit (opcional)'), sha);
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar sessão' }));
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue('');
    expect(gerar).not.toHaveBeenCalled();
  });

  it('não restaura origem confirmada nem autorização de geração ao voltar como leitor', async () => {
    const gerar = abrir();
    await editar();
    expect(screen.getByRole('button', { name: 'Confirmar geração' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Abrir plano' }));
    await userEvent.click(screen.getByRole('button', { name: 'Voltar ao Board' }));
    await userEvent.click(screen.getByRole('button', { name: 'Gerar artefato', exact: true }));
    expect(screen.queryByRole('button', { name: 'Confirmar geração' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Conferir origem' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Revogar edição' }));
    expect(screen.getByRole('button', { name: 'Conferir origem' })).toBeDisabled();
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue(sha);
    expect(gerar).not.toHaveBeenCalled();
  });
});
