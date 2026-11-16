import type { ArtefatosLocais } from '../api/artefatosLocais';
import type { MaquinaRemota } from '../api/remoto';

/** Somente dados fictícios de testes; não iniciam preview nem substituem prova do navegador/backend. */
export function maquinaDoPreview(): MaquinaRemota {
  return { maquinaId: 'host-demo', nome: 'DEMO-PC', versaoDevKit: '170', origem: 'servico', appAberto: true,
    estadoDoAgente: 'parado', turnoEmAndamento: false, conectada: true,
    ultimoHeartbeatEm: new Date().toISOString(), retratoGeradoEm: new Date().toISOString() };
}

export function artefatosLocais(): ArtefatosLocais {
  const agora = new Date().toISOString();
  const fontes = ['a', 'b', 'c', 'd'].map(c => ({ id: c.repeat(64), nome: `repo-${c}`, branch: 'feature/dev/projeto-demo',
    sha: c.repeat(40), branchBase: 'develop', shaBase: c.repeat(40) }));
  return { contrato: 1, operacoesDisponiveis: true, aviso: '', artefatos: [{
    id: 3, situacao: 'pronto', diagnostico: 'Pacote representativo conferido.', criadoEm: agora, terminadoEm: agora,
    conjuntoHash: 'e'.repeat(64), fontes, alvos: ['App', 'Cli', 'Scheduler'].map(nome => ({ repositorio: fontes[0].id,
      nome: nome + '.csproj', receita: 'dotnet-publish', framework: 'net10.0', runtime: null })),
    hash: 'f'.repeat(64), tamanho: 1024,
    comando: { id: 12, recibo: 'a'.repeat(64), inicio: agora, fim: agora, motivoTermino: null, exit: 0, logParcial: 'Publicação concluída.' },
    previews: [{ receita: fontes[0].id, aplicacao: 'site', ciclo: 'd'.repeat(32), versao: 3, pedido: 'iniciar', situacao: 'pronto',
      nestaMaquina: true, url: 'http://127.0.0.1:4317/', diagnostico: 'Jornada representativa confirmada.', atualizadoEm: agora,
      prontidao: { front: true, api: true, dependencias: true, jornada: true }, componentes: [
        { id: 'front', repositorio: fontes[0].id, situacao: 'pronto', porta: 4317, prontoEm: agora, diagnostico: '', logParcial: 'Front iniciado.' },
        { id: 'api', repositorio: fontes[1].id, situacao: 'pronto', porta: 4318, prontoEm: agora, diagnostico: '', logParcial: 'GET fictício: 200.' },
      ] }],
  }] };
}
