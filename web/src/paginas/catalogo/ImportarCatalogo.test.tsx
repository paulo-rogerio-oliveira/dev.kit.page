import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api/cliente';
import { Catalogos } from './Catalogos';
import { catalogoUau } from '../../testes/servidor';

function tela() {
  const publicar = vi.fn();
  render(<Catalogos token="token" catalogos={[catalogoUau]} aberto={null} aoAbrir={vi.fn()} aoMudarLista={async () => {}} aoPublicar={publicar} tratar={(e) => (e as Error).message} />);
  fireEvent.change(screen.getByLabelText('Slug'), { target: { value: 'novo-catalogo' } });
  fireEvent.change(screen.getByLabelText('Nome'), { target: { value: 'Novo' } });
  return publicar;
}

afterEach(() => vi.restoreAllMocks());

it('importa GitHub, mostra contagens e usa publicação existente na lista', async () => {
  const importar = vi.spyOn(api, 'importarCatalogo').mockResolvedValue({ catalogo: { ...catalogoUau, itens: 3 }, skills: 1, rules: 1, fluxos: 1 });
  const publicar = tela();
  await userEvent.click(screen.getByRole('button', { name: 'GitHub' }));
  fireEvent.change(screen.getByLabelText('Repositório'), { target: { value: 'org/repo' } });
  fireEvent.change(screen.getByLabelText('Branch ou tag'), { target: { value: 'feature/um' } });
  fireEvent.change(screen.getByLabelText('Pasta'), { target: { value: 'catalogo' } });
  await userEvent.click(screen.getByRole('button', { name: 'Importar do GitHub' }));
  expect(importar).toHaveBeenCalledWith('token', { catalogo: { slug: 'novo-catalogo', nome: 'Novo', descricao: '' }, repositorio: 'org/repo', referencia: 'feature/um', pasta: 'catalogo' });
  expect(await screen.findByRole('status')).toHaveTextContent('1 skills, 1 Rules, 1 fluxos');
  await userEvent.click(screen.getByRole('button', { name: `Publicar ${catalogoUau.nome}` }));
  expect(await screen.findByRole('region', { name: `Publicar ${catalogoUau.nome}` })).toBeInTheDocument();
  expect(publicar).not.toHaveBeenCalled();
});

it('envia somente JSON da pasta escolhida com caminhos relativos', async () => {
  const importar = vi.spyOn(api, 'importarCatalogo').mockResolvedValue({ catalogo: catalogoUau, skills: 1, rules: 0, fluxos: 0 });
  tela();
  await userEvent.click(screen.getByRole('button', { name: 'Pasta local' }));
  const arquivo = new File(['{}'], 'manifesto.json');
  Object.defineProperty(arquivo, 'webkitRelativePath', { value: 'meu-catalogo/manifesto.json' });
  Object.defineProperty(arquivo, 'text', { value: async () => '{}' });
  fireEvent.change(screen.getByLabelText(/Escolher pasta/), { target: { files: [arquivo, new File(['x'], 'executar.cmd')] } });
  await userEvent.click(screen.getByRole('button', { name: 'Importar pasta local' }));
  await waitFor(() => expect(importar).toHaveBeenCalledWith('token', { catalogo: { slug: 'novo-catalogo', nome: 'Novo', descricao: '' }, arquivos: [{ caminho: 'manifesto.json', conteudo: '{}' }] }));
  expect(await screen.findByRole('status')).toHaveTextContent('Importado da pasta local');
});

it('bloqueia envio duplicado enquanto importa e preserva os campos na falha', async () => {
  let falhar!: (e: Error) => void;
  vi.spyOn(api, 'importarCatalogo').mockImplementation(() => new Promise((_, reject) => { falhar = reject; }));
  tela();
  await userEvent.click(screen.getByRole('button', { name: 'GitHub' }));
  fireEvent.change(screen.getByLabelText('Repositório'), { target: { value: 'org/repo' } });
  await userEvent.click(screen.getByRole('button', { name: 'Importar do GitHub' }));
  expect(screen.getByRole('button', { name: 'Importando…' })).toBeDisabled();
  falhar(new Error('Manifesto inválido.'));
  expect(await screen.findByRole('alert')).toHaveTextContent('Manifesto inválido.');
  expect(screen.getByLabelText('Repositório')).toHaveValue('org/repo');
  expect(screen.getByRole('button', { name: 'Importar do GitHub' })).toBeEnabled();
});
