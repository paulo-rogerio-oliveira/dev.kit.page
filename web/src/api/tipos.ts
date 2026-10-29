// Os tipos do contrato da API — espelho de api/src/DevKitPage.Contracts/V1 (JSON camelCase).
// O documento OpenAPI da API (/openapi/v1.json) é a fonte: `npm run gerar:tipos` gera o
// openapi.d.ts a partir dele para conferir este arquivo quando o contrato mudar.

/**
 * O papel do usuário (US #381, US #405): o gestor vê só a empresa dele; o dev é o usuário do dev.kit —
 * entra no app e baixa a versão, mas não no painel.
 */
export type Papel = 'admin' | 'gestor' | 'dev';

export interface LoginResponse {
  token: string;
  expiraEm: string;
  deveTrocarSenha: boolean;
  login: string;
  ehAdmin: boolean;
  /** Ausente numa sessão gravada antes da US #381: vale como admin. */
  papel?: Papel;
  empresa?: string | null;
}

export interface MaquinaResumo {
  id: number;
  maquinaId: string;
  apelido: string;
  versaoDevKit: string;
  registradaEm: string;
  ultimoEnvioEm: string | null;
  eventos: number;
}

export interface DiaDeUso {
  dia: string;
  sessoes: number;
  turnos: number;
  fluxos: number;
  ferramentas: number;
  comandosDelegados: number;
  arquivosAlterados: number;
}

export interface QuantidadeResposta {
  de: string;
  ate: string;
  sessoes: number;
  turnos: number;
  fluxos: number;
  ferramentas: number;
  comandosDelegados: number;
  arquivosAlterados: number;
  serieDiaria: DiaDeUso[];
  /** Máquinas distintas com evento no período (US #381). */
  maquinasAtivas: number;
  /** Máquinas registradas até o fim do período, no escopo de quem consulta (US #381). */
  maquinasRegistradas: number;
}

export interface CausaDeFalha {
  causa: string;
  quantidade: number;
}

/** As taxas vêm nulas quando não há denominador — a tela mostra um traço. */
export interface QualidadeResposta {
  de: string;
  ate: string;
  turnos: number;
  turnosComFalha: number;
  taxaDeFalha: number | null;
  duracaoMediaDoTurnoMs: number | null;
  falhasPorCausa: CausaDeFalha[];
  avaliacoes: number;
  notaMedia: number | null;
  objetivosCumpridos: number;
  objetivosRecusados: number;
  razaoCumpridosRecusados: number | null;
  turnosPorObjetivoCumprido: number | null;
  /** O cartão Impasses (US #405); ausente numa API anterior a ele. */
  impasses?: ImpassesResumo | null;
  /** O cartão Árbitro (US #405); ausente numa API anterior a ele. */
  arbitro?: ArbitroResumo | null;
}

/** Quantas vezes um recorte (origem, desfecho, ação, regra) aparece no período. */
export interface ContagemPorRecorte {
  recorte: string;
  quantidade: number;
}

/** Os impasses do fluxo no período (US #405): as médias são nulas sem evento. */
export interface ImpassesResumo {
  detectados: number;
  porOrigem: ContagemPorRecorte[];
  minutosParadoNaDeteccao: number | null;
  destravados: number;
  minutosAteDestravar: number | null;
  comoDestravaram: ContagemPorRecorte[];
}

/** O árbitro no período (US #405): a taxa de correção é nula sem cobrança. */
export interface ArbitroResumo {
  acoes: number;
  cobrancas: number;
  /** A regra é o título da seção do CLAUDE.md; vazia é a cobrança sem seção. */
  cobrancasPorRegra: ContagemPorRecorte[];
  corrigidas: number;
  taxaDeCorrecao: number | null;
  escaladasAoDev: number;
  porAcao: ContagemPorRecorte[];
}

/** A última versão estável do dev.kit (US #405, GET /api/versoes/ultima — anônima). */
export interface VersaoDoDevKit {
  versao: number;
  tag: string;
  nome: string;
  publicadaEm: string;
  destaques: string[];
  tamanhoBytes: number;
  sha256: string | null;
  /**
   * A rota da API que entrega o zip DESTA versão — anônima desde a US #425, com limite por IP. A landing aponta para
   * o alias permanente `/api/versoes/ultima/download` (US #467), que segue a última versão.
   */
  urlDownload: string;
  loginObrigatorio: boolean;
}

/** Um usuário na lista do admin (US #405). A senha nunca vem aqui. */
export interface UsuarioResumo {
  id: number;
  login: string;
  nome: string;
  papel: Papel;
  empresaId: number | null;
  empresa: string | null;
  bloqueado: boolean;
  /** O bloqueio temporário por falhas de login, quando vale. */
  bloqueadoAte: string | null;
  deveTrocarSenha: boolean;
  criadoEm: string;
  /** A licença do dev.kit (US #687); ausente numa API anterior a ela. */
  licenca?: LicencaDoUsuario | null;
}

// ----- US #687: a licença por usuário que o admin define -----

/** O tipo da licença: o teste, a do usuário, a do contrato da empresa, ou a conta de administração. */
export type TipoDaLicenca = 'trial' | 'individual' | 'empresa' | 'admin';

/** A situação da licença — a cor da coluna e o filtro. */
export type SituacaoDaLicenca = 'valida' | 'vencendo' | 'vencida' | 'revogada' | 'sem-prazo' | 'automatica';

export interface LicencaDoUsuario {
  tipo: TipoDaLicenca;
  situacao: SituacaoDaLicenca;
  /** A validade que a renovação respeita; nula sem prazo, revogada ou automática. */
  validaAte: string | null;
  /** Os dias até a validade, por data; negativo depois de vencer. */
  diasRestantes: number | null;
  inicio: string;
  /** A validade é a que o admin definiu para este usuário (e não a do contrato da empresa). */
  definidaPeloAdmin: boolean;
  /** A última renovação do dev.kit deste usuário. */
  ultimoUso: string | null;
}

export type ModoDaValidade = 'somar' | 'ate-data' | 'sem-prazo';

/** O Aplicar validade (e a prévia dele): o motivo é obrigatório ao aplicar. */
export interface DefinicaoDeLicenca {
  modo: ModoDaValidade;
  dias?: number | null;
  ate?: string | null;
  tipo?: TipoDaLicenca | null;
  motivo?: string | null;
}

export interface PreviaDeLicenca {
  validaAte: string | null;
  semPrazo: boolean;
  diasAPartirDeHoje: number | null;
  tipo: TipoDaLicenca;
  mensagem: string;
}

export interface HistoricoDeLicenca {
  em: string;
  acao: 'definida' | 'sem-prazo' | 'revogada' | 'renovada' | 'criada';
  descricao: string;
  motivo: string;
  autor: string;
}

export interface DetalheDaLicenca {
  usuarioId: number;
  login: string;
  nome: string;
  papel: Papel;
  empresa: string | null;
  licenca: LicencaDoUsuario;
  historico: HistoricoDeLicenca[];
}

export interface UsuarioNovo {
  login: string;
  nome: string;
  papel: Papel;
  empresaId: number | null;
}

export interface UsuarioEditado {
  nome: string;
  papel: Papel;
  empresaId: number | null;
}

/** A resposta da criação e da redefinição: a senha temporária vem UMA vez. */
export interface UsuarioComSenha {
  usuario: UsuarioResumo;
  senhaTemporaria: string;
}

export interface EventoDoLog {
  eventId: string;
  maquinaId: number;
  apelido: string;
  tipo: string;
  sessaoId: string;
  quantidade: number;
  valor: number | null;
  detalhe: string;
  em: string;
}

export interface Pagina<T> {
  itens: T[];
  total: number;
  numeroDaPagina: number;
  tamanho: number;
}

/** O pedido de demonstração do formulário da landing (POST público). */
export interface PedidoDeDemonstracao {
  nome: string;
  email: string;
  empresa: string;
  mensagem: string;
  consentimento: boolean;
}

export interface PedidoDeDemonstracaoCriado {
  id: number;
  recebidoEm: string;
}

/** Um pedido na lista do dashboard (rota autenticada). */
export interface DemonstracaoResumo {
  id: number;
  nome: string;
  email: string;
  empresa: string;
  mensagem: string;
  recebidoEm: string;
  consentimentoEm: string;
}

/** Os estados de um grupo de exceção (US #381); "Regrediu" só o servidor põe. */
export type EstadoDoGrupo = 'Novo' | 'Visto' | 'Resolvido' | 'Ignorado' | 'Regrediu';

/** Um grupo de exceção não classificada: as ocorrências da mesma assinatura no período. */
export interface GrupoDeErroResumo {
  id: number;
  assinatura: string;
  tipo: string;
  estado: EstadoDoGrupo;
  resolvidoNaVersao: string | null;
  ocorrencias: number;
  maquinas: number;
  primeiraVersao: string;
  ultimaVersao: string;
  primeiroVistoEm: string;
  ultimoVistoEm: string;
}

export interface OcorrenciaDeErroResumo {
  eventId: string;
  apelido: string;
  versaoDevKit: string;
  trace: string;
  em: string;
}

export interface OcorrenciasNoDia {
  dia: string;
  quantidade: number;
}

/** O detalhe de um grupo — também o corpo da exportação em JSON. */
export interface GrupoDeErroDetalhe {
  grupo: GrupoDeErroResumo;
  trace: string;
  ocorrencias: OcorrenciaDeErroResumo[];
  porDia: OcorrenciasNoDia[];
  versoes: string[];
  maquinas: string[];
}

/** Uma empresa do plano empresarial (US #381) — a lista do admin. */
export interface EmpresaResumo {
  id: number;
  nome: string;
  plano: string;
  assentos: number;
  codigoDeAdesao: string;
  colaboradores: number;
  criadaEm: string;
  /** O login (ou o nome) do gestor da empresa; nulo enquanto ninguém foi convidado (US #461). */
  gestor: string | null;
  /** Os devs ativos que ocupam assento — a mesma conta do login (US #461). */
  assentosEmUso: number;
  /** Os dados básicos e o contrato (US #687); ausentes numa API anterior a ela. */
  dados?: DadosDaEmpresa | null;
}

/** Os dados básicos e o contrato de uma empresa (US #687): o CNPJ vai só com os dígitos. */
export interface DadosDaEmpresa {
  cnpj: string;
  segmento: string;
  cidade: string;
  uf: string;
  contato: string;
  telefone: string;
  site: string;
  /** Até quando vai o contrato (yyyy-mm-dd): a validade da licença dos usuários dela. */
  contratoAte: string | null;
  alteradaEm: string | null;
  alteradaPor: string;
}

/** A edição da empresa (US #687): os assentos nunca abaixo dos em uso. */
export interface EmpresaEditada {
  nome: string;
  plano: string;
  assentos: number;
  cnpj: string;
  segmento: string;
  cidade: string;
  uf: string;
  contato: string;
  telefone: string;
  site: string;
  contratoAte: string | null;
}

export interface EmpresaNova {
  nome: string;
  plano: string;
  assentos: number;
}

/** O gestor convidado: a senha inicial vem UMA vez. */
export interface GestorCriado {
  id: number;
  login: string;
  senhaInicial: string;
}

/** Um colaborador que consentiu (uma máquina vinculada à empresa). */
export interface ColaboradorResumo {
  maquinaId: number;
  colaborador: string;
  apelido: string;
  empresa: string;
  versaoDevKit: string;
  consentiuEm: string;
  ultimoEnvioEm: string | null;
}

/**
 * O ROI de um work item numa máquina (US #387): a foto MAIS RECENTE que o dev.kit dela calculou
 * (`devcli roi`). O mesmo item em duas máquinas são duas linhas.
 */
export interface RoiDoWorkItem {
  maquinaId: number;
  apelido: string;
  /** O nome que o colaborador informou no dev.kit; vazio na máquina anônima. */
  colaborador: string;
  workItem: number;
  tipo: string;
  estado: string;
  de: string;
  ate: string;
  turnosDoAgente: number;
  sessoes: number;
  horas: number;
  horasNoBoard: number;
  horasNoTimesheet: number | null;
  /** Horas sobre turnos; nulo sem turno (a tela mostra um traço). */
  horasPorTurno: number | null;
  /** Da criação ao encerramento; nulo enquanto o item está aberto. */
  leadTimeDias: number | null;
  aberto: boolean;
  pullRequests: number;
  pullRequestsMergeadas: number;
  /** Quando o dev.kit calculou a foto. */
  atualizadoEm: string;
}

/** Os totais do ROI no período (de todas as fotos, não só das da lista). */
export interface RoiTotais {
  itens: number;
  turnos: number;
  horas: number;
  /** A média dos ENCERRADOS; nula sem nenhum. */
  leadTimeMedioDias: number | null;
  pullRequests: number;
  pullRequestsMergeadas: number;
}

export interface RoiResposta {
  de: string;
  ate: string;
  itens: RoiDoWorkItem[];
  totais: RoiTotais;
}

// ----- US #425: o catálogo, a conta, a licença e a compra -----

/** O tipo de um item de catálogo: a skill, a Rule (`plan`, o nome do contrato desde o antigo Tech Plan) ou o fluxo. */
export type TipoDeItem = 'skill' | 'plan' | 'fluxo';

/** Um item da vitrine pública do catálogo básico. */
export interface ItemDaVitrine {
  tipo: TipoDeItem;
  nome: string;
  descricao: string;
  tecnologia: string;
  versao: number;
}

/** A vitrine (GET /api/catalogo/base/vitrine): os itens agrupados por tecnologia. */
export interface Vitrine {
  versao: number;
  publicadaEm: string;
  grupos: { tecnologia: string; itens: ItemDaVitrine[] }[];
}

/** Uma release de um catálogo (catalogo-vN no básico, {slug}-vN nos demais), na aba Releases do admin. */
export interface ReleaseDoCatalogo {
  versao: number;
  tag: string;
  nome: string;
  publicadaEm: string;
  itens: number;
  retirados: number;
  tamanhoBytes: number;
  sha256: string | null;
}

/** Um item do catálogo da empresa, na lista de quem é dela. */
export interface ItemDaEmpresa {
  id: number;
  tipo: TipoDeItem;
  nome: string;
  descricao: string;
  versao: number;
  hash: string;
  autor: string;
  assinantes: number;
  assinado: boolean;
  publicadoEm: string;
  /** Quando foi retirado; nulo enquanto está publicado. */
  retirado: string | null;
  dependencias: string[];
}

/** A conta de quem está logado (GET /api/conta). */
export interface Conta {
  login: string;
  nome: string;
  email: string;
  papel: Papel;
  empresa: string | null;
  /** A empresa de um assento só, da compra individual. */
  individual: boolean;
  assinaturaAtiva: boolean;
  /** O portal do gateway — só na conta individual. */
  urlGerenciarAssinatura: string | null;
  loginObrigatorio: boolean;
}

/** A empresa vista pelo gestor (GET /api/empresa). */
export interface EmpresaDoGestor {
  id: number;
  nome: string;
  plano: string;
  assentos: number;
  assentosOcupados: number;
  codigoDeAdesao: string;
  /** A escolha do gestor; nulo segue o padrão global. */
  loginObrigatorio: boolean | null;
  loginObrigatorioEfetivo: boolean;
}

/** Um usuário da empresa, na lista do gestor. */
export interface UsuarioDaEmpresa {
  id: number;
  login: string;
  nome: string;
  email: string;
  papel: Papel;
  bloqueado: boolean;
  criadoEm: string;
}

/** Um repositório do catálogo da empresa (US #491): o caminho e a descrição que o agente do dev.kit grava. */
export interface RepositorioDaEmpresa {
  /** O caminho como o dev.kit o clona (URL do GitHub ou pasta). */
  origem: string;
  descricao: string;
  /** Quem gravou a última descrição. */
  autor: string;
  atualizadoEm: string;
}

/** O nível de uma convenção (US #491): a precedência no dev.kit é usuário > time > empresa. */
export type NivelDeConvencao = 'empresa' | 'time' | 'usuario';

/** Uma convenção na auditoria do site (US #491), hoje o modelo do nome do branch da task. */
export interface ConvencaoDaEmpresa {
  nivel: NivelDeConvencao;
  /**
   * A convenção, uma por tipo de branch (branch.feature, branch.bug, branch.hotfix, branch.<tipo do item>), ou um tema
   * do contexto do dev.kit (contexto.<tema>, US #550).
   */
  chave: string;
  valor: string;
  /** O time da convenção de nível time; vazio nos outros. */
  time: string;
  /** De quem é a convenção de nível usuário; vazio nos outros. */
  usuario: string;
  /** Quem a gravou por último. */
  autor: string;
  atualizadoEm: string | null;
  /** O id da linha (US #663): é por ele que se promove ou descarta a sugestão. */
  id?: number | null;
  /** De quem é o texto (US #663): a REGRA do usuário ou a SUGESTÃO do agente. Ausente na API antiga: regra. */
  origem?: OrigemDoContexto | null;
  /** A versão da linha (US #663); a promoção a confere (outra → 409). */
  versao?: number | null;
}

/** A origem de um tema do contexto (US #663): o usuário escreve REGRA; o agente registra SUGESTÃO. */
export type OrigemDoContexto = 'usuario' | 'agente';

/** A resposta da adesão e do pedido de link: a mensagem (o link vai por e-mail). */
export interface Mensagem {
  mensagem: string;
}

/** Para onde o checkout leva o comprador. */
export interface CheckoutCriado {
  url: string;
  /** Sem gateway configurado: o checkout simulado, que vai direto à compra concluída. */
  simulado: boolean;
}

// ----- US #461: os catálogos nomeados, editados e publicados pelo site -----

/** Uma empresa relacionada a um catálogo: os usuários dela recebem o catálogo no dev.kit. */
export interface EmpresaVinculada {
  id: number;
  nome: string;
  relacionadaEm: string;
}

/** Um catálogo na gestão do admin (GET /api/catalogos) — o básico vem primeiro. */
export interface CatalogoNomeado {
  origem?: string | null;
  id: number;
  slug: string;
  nome: string;
  descricao: string;
  basico: boolean;
  /** A versão publicada mais nova (0 = nunca publicado). */
  versao: number;
  publicadaEm: string | null;
  /** Os itens ativos (não retirados), publicados ou não. */
  itens: number;
  /** Os itens com rascunho que ainda não chegou às máquinas. */
  pendentes: number;
  empresas: EmpresaVinculada[];
  criadoEm: string;
}

export interface CatalogoNovo {
  slug: string;
  nome: string;
  descricao: string;
}

/** A situação de um item editável em relação à última release do catálogo. */
export type SituacaoDoItem = 'novo' | 'alterado' | 'publicado' | 'retirada-pendente' | 'retirado';

/**
 * Um item editável (skill, Rule ou fluxo). O `conteudo` é o MESMO JSON que o dev.kit grava (SkillJson,
 * TechPlanJson, o envelope devkit-fluxo), como texto: nulo na lista, inteiro no detalhe.
 */
export interface ItemEditavel {
  id: number;
  tipo: TipoDeItem;
  nome: string;
  descricao: string;
  categoria: string;
  hash: string;
  /** A versão na última release (0 = nunca publicado). */
  versaoPublicada: number;
  situacao: SituacaoDoItem;
  alteradoEm: string;
  alteradoPor: string;
  /** O que o item usa (`tipo:nome`). */
  dependencias: string[];
  conteudo: string | null;
  /** O slug do catálogo do item (US #687); ausente numa API anterior. */
  catalogo?: string | null;
  /** Para onde o item vai: o básico (base), uma empresa, ou preso a um repositório (projeto). */
  escopo?: EscopoDoItem | null;
  /** A stack da Rule (vazia em qualquer stack e nos outros tipos). */
  stack?: string | null;
  /** A Rule é de impacto: nunca vai para o básico. */
  ruleDeImpacto?: boolean;
}

// ----- US #687: a edição de artefato do catálogo -----

export type EscopoDoItem = 'base' | 'empresa' | 'projeto';

export interface LinhaDaDiferenca {
  tipo: 'igual' | 'adicionada' | 'removida';
  texto: string;
  /** O número da linha no rascunho (nulo na removida). */
  numero: number | null;
}

export interface DiferencasDoItem {
  versaoPublicada: number;
  publicado: string;
  rascunho: string;
  linhas: LinhaDaDiferenca[];
  adicionadas: number;
  removidas: number;
}

export interface HistoricoDoItem {
  em: string;
  acao: 'rascunho' | 'descartado' | 'retirado' | 'publicado';
  versao: number;
  release: string;
  autor: string;
}

export interface AnexoDoItem {
  id: number;
  nome: string;
  tipoDeConteudo: string;
  tamanhoBytes: number;
  autor: string;
  em: string;
}

/** Criar ou editar um item: o conteúdo é o JSON no formato do dev.kit; a categoria vazia é a derivada do nome. */
export interface ItemEditado {
  tipo: TipoDeItem;
  conteudo: string;
  categoria?: string;
}

/** A conferência de uma publicação — também o corpo do 409 quando ela é recusada. */
export interface RelatorioDePublicacao {
  slug: string;
  versaoAtual: number;
  proxima: number;
  podePublicar: boolean;
  minDevKit: number;
  /** '+ skill:x v1', '~ plan:y v2', '- fluxo:z (retirado)', '· skill:w (dados da vitrine)'. */
  mudancas: string[];
  problemas: string[];
}

export interface PublicacaoDoCatalogo {
  release: ReleaseDoCatalogo;
  relatorio: RelatorioDePublicacao;
}

/** A importação única da release catalogo-vN do GitHub para o básico do site. */
export interface ImportacaoDoBasico {
  versao: number;
  itens: number;
  retirados: number;
  importada: boolean;
  mensagem: string;
}

/** Um catálogo que o dev.kit de quem consulta sincroniza (GET /api/catalogos/meus). */
export interface CatalogoDisponivel {
  slug: string;
  nome: string;
  basico: boolean;
  versao: number;
  publicadaEm: string | null;
}

// ----- US #602: os relatos dos usuários do dev.kit -----

/** A categoria de um relato (CategoriasDeRelato). */
export type CategoriaDeRelato = 'defeito' | 'sugestao' | 'duvida';

/** O estado de um relato (EstadosDeRelato): chega novo, e quem administra o leva a em análise e a resolvido. */
export type EstadoDoRelato = 'novo' | 'em-analise' | 'resolvido';

/** O relato que o dev.kit manda (POST /api/relatos): o usuário e a empresa vêm do token. */
export interface RelatoNovo {
  categoria: CategoriaDeRelato;
  texto: string;
  contexto?: string | null;
  /** PNG ou JPEG em base64. */
  captura?: string | null;
  versaoDevKit?: string | null;
  /** O id da fila do dev.kit: o reenvio devolve o mesmo relato. */
  chaveDoCliente?: string | null;
}

export interface RelatoRecebido {
  id: number;
  estado: EstadoDoRelato;
  recebidoEm: string;
}

/** Um relato na lista da gestão (o admin geral e o gestor da empresa). */
export interface RelatoResumo {
  id: number;
  categoria: CategoriaDeRelato;
  estado: EstadoDoRelato;
  /** O começo do texto. */
  resumo: string;
  autor: string;
  empresaId: number | null;
  empresa: string | null;
  criadoEm: string;
  atualizadoEm: string;
  temCaptura: boolean;
  respondido: boolean;
}

/** O relato inteiro, no detalhe da gestão. */
export interface RelatoDetalhe {
  id: number;
  categoria: CategoriaDeRelato;
  estado: EstadoDoRelato;
  texto: string;
  /** Mascarado; vazio quando o usuário não consentiu. */
  contexto: string;
  /** Base64; nula sem captura. */
  captura: string | null;
  versaoDevKit: string;
  autor: string;
  empresaId: number | null;
  empresa: string | null;
  criadoEm: string;
  atualizadoEm: string;
  /** Vazia enquanto não houver. */
  resposta: string;
  respondidoPor: string;
  respondidoEm: string | null;
}

/** Um relato do próprio usuário (GET /api/relatos/meus). */
export interface MeuRelato {
  id: number;
  categoria: CategoriaDeRelato;
  estado: EstadoDoRelato;
  texto: string;
  criadoEm: string;
  atualizadoEm: string;
  resposta: string;
  respondidoEm: string | null;
  chaveDoCliente: string | null;
}

/** O filtro da lista de relatos: vazio (ou nulo) é sem filtro; a empresa só vale para o admin; de/até em AAAA-MM-DD. */
export interface FiltroDeRelatos {
  estado: EstadoDoRelato | '';
  categoria: CategoriaDeRelato | '';
  empresa: number | null;
  de: string;
  ate: string;
}

// ----- US #669: o design compartilhado -----

/** O tipo de uma versão de design: a tela real ou a proposta (imagem, nada implementado). */
export type TipoDeVersaoDeDesign = 'captura' | 'proposta';

/** Uma versão imutável de uma tela (o contrato VersaoDeDesignV1). A imagem vem pelo `link` estável, com o token. */
export interface VersaoDeDesign {
  numero: number;
  tipo: TipoDeVersaoDeDesign;
  base: number | null;
  tema: 'light' | 'dark';
  largura: number;
  altura: number;
  /** web, desktop ou proposta. */
  origem: string;
  alvo: string;
  /** O branch de onde a versão saiu — atributo, e não projeto. */
  branch: string;
  commit: string;
  descricao: string;
  autor: string;
  autorTipo: 'usuario' | 'agente';
  runner: string;
  criadaEm: string;
  sha256: string;
  bytes: number;
  /** O caminho da imagem na API (/api/design/{projeto}/{tela}/v{n}); a página do site é o mesmo sem o /api. */
  link: string;
  podeApagar: boolean;
}

/** Uma tela do projeto, com as versões em ordem. */
export interface TelaDeDesign {
  id: string;
  nome: string;
  estado: string;
  versoes: VersaoDeDesign[];
}

/** Um projeto de design: as telas de UM repositório num nível (empresa, time ou usuário). */
export interface ProjetoDeDesign {
  id: number;
  repositorio: string;
  /** O caminho normalizado (github.com/acme/portal). */
  chave: string;
  nivel: NivelDeConvencao;
  time: string;
  dono: string;
  atualizadoEm: string;
  telas: TelaDeDesign[];
}

/** O uso do teto do plano: a empresa de quem consulta, ou ele mesmo sem empresa. */
export interface UsoDoDesign {
  versoes: number;
  tetoDeVersoes: number;
  bytes: number;
  tetoDeBytes: number;
  plano: string;
}

/** Um arquivo baixado da API: o conteúdo e o nome sugerido. */
export interface ArquivoBaixado {
  conteudo: Blob;
  nome: string;
}

/** O filtro do dashboard: período (AAAA-MM-DD, inclusive) e máquina (vazio = todas). */
export interface Filtro {
  de: string;
  ate: string;
  maquina: number | null;
}

// ----- US #687: o painel de utilidade -----

/** Um cartão do "Saúde agora". */
export interface CartaoDeSaude {
  quantidade: number;
  novos24h: number;
  /** Desde quando está o caso mais antigo. */
  maisAntigo: string | null;
  /** Os work items do fechamento (#669), a amostra da máquina… */
  itens: string[];
}

export interface SaudeAgora {
  tasksParadas: CartaoDeSaude;
  perguntas: CartaoDeSaude;
  fechamentos: CartaoDeSaude;
  maquinas: CartaoDeSaude;
}

export interface ResultadoDoUso {
  aprovadas: number;
  integradas: number;
  aprovadasNaSemana: number;
  percentualNaPrimeira: number | null;
  variacaoNaPrimeira: number | null;
  notaMedia: number | null;
  rodadasAteANota: number | null;
  medianaAteAprovacaoMinutos: number | null;
  variacaoDaMedianaMinutos: number | null;
  percentualEsperandoDev: number | null;
  turnosPorEntrega: number | null;
  turnos: number;
  comandosDelegados: number;
  arquivosAlterados: number;
}

export interface TurnosDoDia {
  dia: string;
  trabalhou: number;
  esperouDev: number;
  limiteDeUso: number;
  falhou: number;
}

export type TipoDoSelo = 'sugestao' | 'corrigido' | 'us' | '';

export interface CausaDeTravamento {
  causa: string;
  vezes: number;
  selo: string;
  tipoDoSelo: TipoDoSelo;
}

/** O painel de utilidade (GET /api/dashboard/utilidade). */
export interface UtilidadeResposta {
  de: string;
  ate: string;
  saude: SaudeAgora;
  resultado: ResultadoDoUso;
  turnosPorDesfecho: TurnosDoDia[];
  ondeTravam: CausaDeTravamento[];
}

/** O harness — o CLI do provedor cognitivo que roda o turno — dos filtros do log e das exceções (o codex desde a versão 5). */
export type Harness = 'claude' | 'kiro' | 'glm' | 'codex';

/** O recorte do log de eventos: o tipo e o detalhe exatos e o harness da sessão — vazio é sem recorte. */
export interface RecorteDoLog {
  tipo?: string;
  detalhe?: string;
  harness?: Harness | null;
}
