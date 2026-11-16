import { render, screen } from '@testing-library/react';
import { ArtefatosDoCard } from './ArtefatosDoProjeto';
import type { Artefato } from '../api/projetos';
import { artefatosLocais } from '../testes/artefatosLocais';

function dados() {
  const locais = artefatosLocais();
  const g = locais.artefatos[0];
  g.situacao = 'falhou'; g.previews = []; g.hash = ''; g.tamanho = 0;
  const pacote = { repositorio: g.fontes[0].id, sha: g.fontes[0].sha, versao: '170', hash: 'f'.repeat(64),
    tamanho: 2048, idRemoto: 501, validoAte: '2020-01-01T00:00:00Z' };
  g.pacote = pacote;
  const agora = Date.parse('2026-11-16T10:00:00Z');
  const publicado: Artefato = { id: 501, projetoId: 11, tipo: 'pacote', repositorio: 'https://github.com/demo/site',
    sha: pacote.sha, hash: pacote.hash, tamanho: 2048, link: 'https://downloads.invalid/autorizado.zip?sig=renovada',
    expiraEm: '2026-11-16T10:30:00Z', ambiente: '', autor: 'Dev', criadoEm: '2026-11-16T09:00:00Z' };
  return { locais, pacote, publicado, agora };
}

describe('vínculo de publicações com o conjunto do host', () => {
  it('mostra pins, exclusões, saídas e opções capturadas na consulta existente', () => {
    const { locais, publicado, agora } = dados();
    const g = locais.artefatos[0];
    g.alvos[0].saida = 'produtos/app'; g.alvos[0].assembly = 'App';
    g.detalhes = { politicaDePins: 'conteudo-identico',
      pins: [{ consumidor: g.fontes[0].id, papel: 'catalogo', repositorio: g.fontes[2].id,
        declarado: 'e'.repeat(40), capturado: g.fontes[2].sha, arvoreDeclarada: 'f'.repeat(40),
        arvoreCapturada: 'f'.repeat(40), raiz: 'catalogo/catalogo', politica: 'conteudo-identico' }],
      exclusoes: [{ repositorio: g.fontes[0].id, caminho: 'tools/Aux.csproj', motivo: 'seleção explícita' }],
      opcoes: { repositorioPrincipal: g.fontes[0].id, formatoSolicitado: 'pacote',
        previewRemoto: { app: 'app-capturado', grupo: 'grupo-capturado', assinatura: 'assinatura-capturada', tenant: 'tenant-capturado' } } };
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[publicado]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    expect(screen.getByText('produtos/app')).toBeInTheDocument();
    expect(screen.getByText(/Pin catalogo/)).toBeInTheDocument();
    expect(screen.getByText('catalogo/catalogo')).toBeInTheDocument();
    expect(screen.getByText(/tools\/Aux.csproj.*seleção explícita/)).toBeInTheDocument();
    expect(screen.getByText(/Destino SWA registrado · app-capturado/)).toBeInTheDocument();
    expect(screen.queryByText('Chamada ao backend conferida')).not.toBeInTheDocument();
  });
  it('usa identidade exata do pacote parcial e o link/prazo atuais da consulta autorizada', () => {
    const { locais, publicado, agora } = dados();
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[publicado]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    const link = screen.getByRole('link', { name: /Baixar o pacote de site.*geração 3/ });
    expect(link).toHaveAttribute('href', publicado.link);
    expect(link).toHaveTextContent('Geração nº 3');
    expect(link).toHaveTextContent('expira em 30 min');
    expect(screen.getByText('falhou')).toBeInTheDocument();
    expect(screen.getByText(/Pacote conferido · versão 170/)).toBeInTheDocument();
    expect(screen.queryByText('Chamada ao backend conferida')).not.toBeInTheDocument();
  });

  it.each([
    ['idRemoto', 999], ['hash', 'b'.repeat(64)], ['tamanho', 2049], ['sha', 'b'.repeat(40)],
  ])('não atribui geração ao download com %s divergente', (campo, valor) => {
    const { locais, pacote, publicado, agora } = dados();
    Object.assign(pacote, { [campo]: valor });
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[publicado]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    const link = screen.getByRole('link', { name: /Baixar o pacote de site/ });
    expect(link).not.toHaveTextContent('Geração nº 3');
    expect(link).toHaveAttribute('href', publicado.link);
  });

  it('metadado do retrato não fornece URL nem cria download quando a API não retorna a publicação', () => {
    const { locais, pacote, agora } = dados();
    Object.assign(pacote, { link: 'https://nao-autorizado.invalid/pacote.zip' });
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    expect(screen.queryAllByRole('link')).toHaveLength(0);
    expect(screen.queryByText(/nao-autorizado/)).not.toBeInTheDocument();
  });

  it('duas gerações com a mesma alegação não inventam associação única', () => {
    const { locais, publicado, agora } = dados();
    locais.artefatos.push({ ...locais.artefatos[0], id: 4 });
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[publicado]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    expect(screen.getByRole('link', { name: /Baixar o pacote de site/ })).not.toHaveTextContent('Geração nº');
  });

  it('publicação SWA e pacote têm IDs e links distintos, vinculados ao mesmo conjunto', () => {
    const { locais, publicado, agora } = dados();
    locais.artefatos[0].situacao = 'pronto';
    locais.artefatos[0].publicacao = { idRemoto: 502, tipo: 'preview', repositorio: locais.artefatos[0].fontes[0].id,
      sha: publicado.sha, hash: '', tamanho: 0 };
    const swa: Artefato = { ...publicado, id: 502, tipo: 'preview', hash: '', tamanho: 0,
      link: 'https://demo.azurestaticapps.net', expiraEm: null, ambiente: 'demo' };
    render(<ArtefatosDoCard nome="Demo" slug="demo" locais={locais} artefatos={[publicado, swa]} gerar={() => undefined}
      desabilitado={false} motivo="" agora={agora} />);
    expect(screen.getByRole('link', { name: /Baixar o pacote de site.*geração 3/ })).toHaveAttribute('href', publicado.link);
    expect(screen.getByRole('link', { name: /Abrir o preview de site.*geração 3/ })).toHaveAttribute('href', swa.link);
  });
});
